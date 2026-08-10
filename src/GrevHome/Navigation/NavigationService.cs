namespace GrevHome.Navigation;

public sealed class NavigationService
{
    private readonly Stack<Route> _history = new();

    public Route Current { get; private set; } = Route.Login;

    public event Action<Route>? RouteChanged;

    public void Reset(Route route)
    {
        _history.Clear();
        Current = route;
        RouteChanged?.Invoke(route);
    }

    public void Navigate(Route route, bool allowSameRoute = false)
    {
        if (Current == route && !allowSameRoute)
        {
            return;
        }

        _history.Push(Current);
        Current = route;
        RouteChanged?.Invoke(route);
    }

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

        Current = _history.Pop();
        RouteChanged?.Invoke(Current);
        return true;
    }
}
