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
    private readonly Dictionary<Route, RouteFocusBookmark> _routeFocusBookmarks = new();
    private NavigationTransition? _pendingShellNavigationTransition;
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
        // A same-route modal/action-menu push captures the parent route's current focus before the
        // modal takes it. When Back later closes that modal, do not overwrite the saved parent
        // bookmark with the modal button that currently has focus.
        if (!(transition.Kind == NavigationTransitionKind.Back && transition.From == transition.To))
        {
            CaptureRouteFocus(transition.From);
        }

        _pendingShellNavigationTransition = transition;
    }

    private void HandleShellRouteChanged(Route route)
    {
        UpdateShellBackButtonState();

        var transition = _pendingShellNavigationTransition;
        var requestVersion = ++_shellFocusRequestVersion;

        // Existing page integrations render synchronously from RouteChanged and most schedule their
        // own ContextIdle focus. Run after those page-level callbacks so the shell has final say on
        // the controller focus/viewport contract.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => ApplyShellNavigationLanding(route, transition, requestVersion)));
    }

    private void UpdateShellBackButtonState()
    {
        var route = _navigation.Current;

        // History is the normal source of truth. Login keeps its legacy escape back to Dashboard
        // when users are signed in even if a caller intentionally reset navigation to Login.
        ShellBackButton.IsEnabled = _navigation.CanGoBack ||
                                    (route == Route.Login && _session.HasSignedInUsers);
    }

    private void ApplyShellNavigationLanding(
        Route route,
        NavigationTransition? transition,
        long requestVersion)
    {
        if (requestVersion != _shellFocusRequestVersion || _navigation.Current != route)
        {
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

        if (transition?.Kind == NavigationTransitionKind.Back && TryRestoreRouteFocus(route))
        {
            return;
        }

        // A fresh route starts predictably at its beginning. Back intentionally does not reset the
        // viewport, so returning from a detail/settings page feels like returning to the same place.
        if (transition?.Kind is NavigationTransitionKind.Forward or NavigationTransitionKind.Reset)
        {
            ScrollRouteToTop();
        }

        FocusFirstRouteControl();
    }

    private void CaptureRouteFocus(Route route)
    {
        // Grev Home's route boundary is controller-first. Remember actual selectable Buttons rather
        // than generic Focusable FrameworkElements so a ScrollViewer or text surface can never
        // become the accidental landing target of a new controller route.
        if (Keyboard.FocusedElement is not Button focused ||
            !RouteHost.IsAncestorOf(focused))
        {
            return;
        }

        var focusables = GetRouteFocusableElements();
        var index = focusables.IndexOf(focused);
        if (index < 0)
        {
            return;
        }

        _routeFocusBookmarks[route] = new RouteFocusBookmark(
            new WeakReference<Button>(focused),
            index);
    }

    private bool TryRestoreRouteFocus(Route route)
    {
        if (!_routeFocusBookmarks.TryGetValue(route, out var bookmark))
        {
            return false;
        }

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

        // Dynamic Store/library/profile lists can recreate their buttons while a detail page is
        // open. Falling back to the same focusable index restores the user's approximate position
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
