namespace GrevHome.Navigation;

public enum NavigationTransitionKind
{
    Reset,
    Forward,
    Back,
    SameRoutePush,
    SameRouteBack
}

public sealed record NavigationTransition(
    Route From,
    Route To,
    NavigationTransitionKind Kind);

public sealed class NavigationService
{
    private readonly Stack<NavigationHistoryEntry> _history = new();

    public Route Current { get; private set; } = Route.Login;
    public bool CanGoBack => _history.Count > 0;
    public int HistoryDepth => _history.Count;
    public NavigationTransition? LastTransition { get; private set; }

    /// <summary>
    /// Raised before Current/history are changed, while the outgoing route is still presented.
    /// Shell consumers use this to capture focus/viewport state without coupling pages to navigation.
    /// </summary>
    public event Action<NavigationTransition>? RouteChanging;

    /// <summary>
    /// Compatibility route notification used by the existing page integrations.
    /// </summary>
    public event Action<Route>? RouteChanged;

    public void Reset(Route route)
    {
        var transition = new NavigationTransition(Current, route, NavigationTransitionKind.Reset);
        RouteChanging?.Invoke(transition);

        _history.Clear();
        Current = route;
        LastTransition = transition;
        RouteChanged?.Invoke(route);
    }

    /// <summary>
    /// Navigates to another route. allowSameRoute is reserved for same-route modal/action-menu
    /// history: Back from that entry is reported as SameRouteBack so the shell can restore the
    /// parent page focus without applying fresh-page behavior.
    /// </summary>
    public void Navigate(Route route, bool allowSameRoute = false)
    {
        if (Current == route && !allowSameRoute)
        {
            return;
        }

        var sameRoute = Current == route;
        var kind = sameRoute
            ? NavigationTransitionKind.SameRoutePush
            : NavigationTransitionKind.Forward;
        var returnKind = sameRoute
            ? NavigationTransitionKind.SameRouteBack
            : NavigationTransitionKind.Back;
        var transition = new NavigationTransition(Current, route, kind);
        RouteChanging?.Invoke(transition);

        _history.Push(new NavigationHistoryEntry(Current, returnKind));
        Current = route;
        LastTransition = transition;
        RouteChanged?.Invoke(route);
    }

    /// <summary>
    /// Pushes a new piece of content that intentionally lives on the same shell route, such as
    /// entering another Files directory. It behaves like normal Forward/Back navigation rather
    /// than like a modal even though the Route enum value itself does not change.
    /// </summary>
    public void NavigateWithinRoute(Route route)
    {
        if (Current != route)
        {
            throw new InvalidOperationException(
                $"NavigateWithinRoute can only be used for the current route. Current={Current}, requested={route}.");
        }

        var transition = new NavigationTransition(Current, route, NavigationTransitionKind.Forward);
        RouteChanging?.Invoke(transition);

        _history.Push(new NavigationHistoryEntry(Current, NavigationTransitionKind.Back));
        LastTransition = transition;
        RouteChanged?.Invoke(route);
    }

    public void PushCurrentBackEntry() =>
        _history.Push(new NavigationHistoryEntry(Current, NavigationTransitionKind.Back));

    public bool DiscardBackEntry(Route expectedRoute)
    {
        if (_history.Count == 0 || _history.Peek().Route != expectedRoute)
        {
            return false;
        }

        _history.Pop();
        return true;
    }

    public bool GoBack()
    {
        if (_history.Count == 0)
        {
            return false;
        }

        var target = _history.Peek();
        var transition = new NavigationTransition(Current, target.Route, target.ReturnKind);
        RouteChanging?.Invoke(transition);

        Current = _history.Pop().Route;
        LastTransition = transition;
        RouteChanged?.Invoke(Current);
        return true;
    }

    private sealed record NavigationHistoryEntry(
        Route Route,
        NavigationTransitionKind ReturnKind);
}
