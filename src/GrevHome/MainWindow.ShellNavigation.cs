using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GrevHome.Navigation;

namespace GrevHome;

/// <summary>
/// Milestone 0.15 shell-level navigation behaviour.
///
/// Pages remain responsible for rendering their own content, but the permanent MainWindow shell
/// owns route history semantics, focus restoration and fresh-page viewport positioning. This keeps
/// controller behaviour consistent without teaching every page about every other page.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<int, RouteFocusBookmark> _historyFocusBookmarks = new();
    private NavigationTransition? _pendingShellNavigationTransition;
    private RouteFocusBookmark? _pendingBackFocusBookmark;
    private long _shellFocusRequestVersion;
    private bool _shellNavigationFinalizationReady;

    private void InitializeShellNavigationFinalization()
    {
        if (_shellNavigationFinalizationReady)
        {
            return;
        }

        _shellNavigationFinalizationReady = true;
        _navigation.RouteChanging += HandleShellRouteChanging;
        _navigation.RouteChanged += HandleShellRouteChanged;
        _session.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateShellBackButtonState));
    }

    private void HandleShellRouteChanging(NavigationTransition transition)
    {
        _pendingBackFocusBookmark = null;

        switch (transition.Kind)
        {
            case NavigationTransitionKind.Reset:
                _historyFocusBookmarks.Clear();
                break;

            case NavigationTransitionKind.Forward:
            case NavigationTransitionKind.SameRoutePush:
                if (TryCaptureRouteFocus(out var bookmark))
                {
                    // RouteChanging runs before NavigationService pushes the history entry. The
                    // bookmark therefore belongs to the depth we are about to enter.
                    _historyFocusBookmarks[_navigation.HistoryDepth + 1] = bookmark;
                }
                break;

            case NavigationTransitionKind.Back:
            case NavigationTransitionKind.SameRouteBack:
                // Never capture the currently focused modal/child-page control while leaving it.
                // Restore the exact parent bookmark that was saved when this history entry opened.
                if (_historyFocusBookmarks.Remove(_navigation.HistoryDepth, out var returningBookmark))
                {
                    _pendingBackFocusBookmark = returningBookmark;
                }
                break;
        }

        _pendingShellNavigationTransition = transition;
    }

    private void HandleShellRouteChanged(Route route)
    {
        UpdateShellBackButtonState();

        var transition = _pendingShellNavigationTransition;
        AnimateRouteTransition(transition);
        var backBookmark = _pendingBackFocusBookmark;
        _pendingBackFocusBookmark = null;
        var requestVersion = ++_shellFocusRequestVersion;

        // Existing page integrations render synchronously from RouteChanged and most schedule their
        // own ContextIdle focus. Run after those page-level callbacks so the shell has final say on
        // the controller focus/viewport contract.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => ApplyShellNavigationLanding(route, transition, backBookmark, requestVersion)));
    }

    private void UpdateShellBackButtonState()
    {
        var route = _navigation.Current;

        // History is the normal source of truth. Login keeps its legacy escape back to Dashboard
        // when users are signed in even if a caller intentionally reset navigation to Login.
        var canGoBack = _navigation.CanGoBack ||
                        (route == Route.Login && _session.HasSignedInUsers);
        ShellBackButton.IsEnabled = canGoBack;
        ShellBackButton.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyShellNavigationLanding(
        Route route,
        NavigationTransition? transition,
        RouteFocusBookmark? backBookmark,
        long requestVersion)
    {
        if (requestVersion != _shellFocusRequestVersion || _navigation.Current != route)
        {
            return;
        }

        // A shared controller keyboard is a real modal. It owns the route's landing focus even if
        // the route itself has just opened and the shell's delayed focus pass runs afterwards.
        if (GetOpenControllerKeyboard() is { } controllerKeyboard)
        {
            controllerKeyboard.FocusInitial();
            return;
        }

        if (IsStoreModalOpen || IsPowerMenuOpen || _overlayWindow.IsOpen)
        {
            return;
        }

        // Same-route pushes are used to put an in-page modal/action menu on the Back stack. The
        // local modal owns its own initial focus; shell landing must not steal focus back to page 1.
        if (transition?.Kind == NavigationTransitionKind.SameRoutePush)
        {
            return;
        }

        if (transition?.Kind is NavigationTransitionKind.Back or NavigationTransitionKind.SameRouteBack)
        {
            if (backBookmark is not null && TryRestoreRouteFocus(backBookmark))
            {
                return;
            }

            // Back preserves the existing viewport even if the original control no longer exists.
            FocusFirstRouteControl();
            return;
        }

        // A fresh route starts predictably at its beginning. This includes same-route content
        // navigation such as entering a Files directory through NavigateWithinRoute.
        if (transition?.Kind is NavigationTransitionKind.Forward or NavigationTransitionKind.Reset)
        {
            ScrollRouteToTop();
        }

        FocusFirstRouteControl();
    }

    private bool TryCaptureRouteFocus(out RouteFocusBookmark bookmark)
    {
        bookmark = default!;

        // Grev Home's route boundary is controller-first. Remember actual selectable Buttons rather
        // than generic Focusable FrameworkElements so a ScrollViewer or text surface can never
        // become the accidental landing target of a new controller route.
        if (Keyboard.FocusedElement is not Button focused ||
            !RouteHost.IsAncestorOf(focused))
        {
            return false;
        }

        var focusables = GetRouteFocusableElements();
        var index = focusables.IndexOf(focused);
        if (index < 0)
        {
            return false;
        }

        bookmark = new RouteFocusBookmark(
            new WeakReference<Button>(focused),
            index);
        return true;
    }

    private bool TryRestoreRouteFocus(RouteFocusBookmark bookmark)
    {
        var focusables = GetRouteFocusableElements();
        if (focusables.Count == 0)
        {
            return false;
        }

        if (bookmark.Target.TryGetTarget(out var previous) &&
            focusables.Contains(previous) &&
            IsFocusableButton(previous))
        {
            return previous.Focus();
        }

        // Dynamic Store/library/profile/file lists can recreate their buttons while a child route
        // is open. Falling back to the same focusable index restores the user's approximate position
        // instead of dumping controller focus at the first tile.
        var fallbackIndex = Math.Clamp(bookmark.FocusableIndex, 0, focusables.Count - 1);
        return focusables[fallbackIndex].Focus();
    }

    private void ScrollRouteToTop()
    {
        foreach (var scrollViewer in FindVisualChildren<ScrollViewer>(RouteHost)
                     .Where(viewer => viewer.IsVisible))
        {
            scrollViewer.ScrollToTop();
            scrollViewer.ScrollToLeftEnd();
        }
    }

    private void FocusFirstRouteControl()
    {
        if (GetOpenControllerKeyboard() is { } controllerKeyboard)
        {
            controllerKeyboard.FocusInitial();
            return;
        }

        GetRouteFocusableElements().FirstOrDefault()?.Focus();
    }

    private List<Button> GetRouteFocusableElements() =>
        FindVisualChildren<Button>(RouteHost)
            .Where(IsFocusableButton)
            .ToList();

    private sealed record RouteFocusBookmark(
        WeakReference<Button> Target,
        int FocusableIndex);
}
