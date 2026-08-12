namespace GrevHome.Navigation;

public enum NavigationTransitionKind
{
    Reset,
    Forward,
    Back,
    SameRoutePush
}

public sealed record NavigationTransition(
    Route From,
    Route To,
    NavigationTransitionKind Kind);

public sealed class NavigationService
{
    private readonly Stack<Route> _history = new();

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

    public void Navigate(Route route, bool allowSameRoute = false)
    {
        if (Current == route && !allowSameRoute)
        {
            return;
        }

        var kind = Current == route
            ? NavigationTransitionKind.SameRoutePush
            : NavigationTransitionKind.Forward;
        var transition = new NavigationTransition(Current, route, kind);
        RouteChanging?.Invoke(transition);

        _history.Push(Current);
        Current = route;
        LastTransition = transition;
        RouteChanged?.Invoke(route);
    }

    public void PushCurrentBackEntry() => _history.Push(Current);

    public bool DiscardBackEntry(Route expectedRoute)
    {
        if (_history.Count == 0 || _history.Peek() != expectedRoute)
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
        var transition = new NavigationTransition(Current, target, NavigationTransitionKind.Back);
        RouteChanging?.Invoke(transition);

        Current = _history.Pop();
        LastTransition = transition;
        RouteChanged?.Invoke(Current);
        return true;
    }
}
