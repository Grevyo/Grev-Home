using System.Collections.ObjectModel;
using GrevHome.Profiles;

namespace GrevHome.Sessions;

public enum AccountKind
{
    Local,
    Guest,
    GrevDad
}

public sealed class SessionUser
{
    public Guid SessionId { get; } = Guid.NewGuid();
    public string? GrevId { get; }
    public string? Username { get; }
    public string DisplayName { get; }
    public AccountKind AccountKind { get; }
    public bool IsPrimary { get; internal set; }

    public SessionUser(string? grevId, string? username, string displayName, AccountKind accountKind)
    {
        GrevId = grevId;
        Username = username;
        DisplayName = displayName;
        AccountKind = accountKind;
    }
}

public sealed record ControllerAssignment(int ControllerIndex, Guid SessionUserId);

public sealed class SessionContext
{
    public ObservableCollection<SessionUser> SignedInUsers { get; } = new();
    public ObservableCollection<ControllerAssignment> ControllerAssignments { get; } = new();

    public SessionUser? PrimaryUser => SignedInUsers.FirstOrDefault(user => user.IsPrimary);
    public bool HasSignedInUsers => SignedInUsers.Count > 0;

    public event EventHandler? Changed;

    public SessionUser SignInLocal(LocalProfile profile, int? controllerIndex = null)
    {
        var user = SignedInUsers.FirstOrDefault(candidate =>
            string.Equals(candidate.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            user = new SessionUser(profile.GrevId, profile.Username, profile.DisplayName, AccountKind.Local)
            {
                IsPrimary = SignedInUsers.Count == 0
            };
            SignedInUsers.Add(user);
        }

        if (controllerIndex.HasValue)
        {
            AssignControllerInternal(controllerIndex.Value, user.SessionId);
        }

        RaiseChanged();
        return user;
    }

    public SessionUser SignInGuest(int? controllerIndex = null)
    {
        var guest = SignedInUsers.FirstOrDefault(candidate => candidate.AccountKind == AccountKind.Guest);
        if (guest is null)
        {
            guest = new SessionUser(null, null, "Guest", AccountKind.Guest)
            {
                IsPrimary = SignedInUsers.Count == 0
            };
            SignedInUsers.Add(guest);
        }

        if (controllerIndex.HasValue)
        {
            AssignControllerInternal(controllerIndex.Value, guest.SessionId);
        }

        RaiseChanged();
        return guest;
    }

    public void SetPrimary(Guid sessionUserId)
    {
        var requested = SignedInUsers.FirstOrDefault(user => user.SessionId == sessionUserId)
            ?? throw new InvalidOperationException("That user is not signed in.");

        foreach (var user in SignedInUsers)
        {
            user.IsPrimary = user.SessionId == requested.SessionId;
        }

        RaiseChanged();
    }

    public void AssignController(int controllerIndex, Guid sessionUserId)
    {
        if (SignedInUsers.All(user => user.SessionId != sessionUserId))
        {
            throw new InvalidOperationException("That user is not signed in.");
        }

        AssignControllerInternal(controllerIndex, sessionUserId);
        RaiseChanged();
    }

    public SessionUser? GetUserForController(int controllerIndex)
    {
        var assignment = ControllerAssignments.FirstOrDefault(item => item.ControllerIndex == controllerIndex);
        return assignment is null
            ? null
            : SignedInUsers.FirstOrDefault(user => user.SessionId == assignment.SessionUserId);
    }

    public IReadOnlyList<int> GetControllersForUser(Guid sessionUserId) =>
        ControllerAssignments
            .Where(item => item.SessionUserId == sessionUserId)
            .Select(item => item.ControllerIndex)
            .OrderBy(index => index)
            .ToArray();

    public void SignOut(Guid sessionUserId)
    {
        var user = SignedInUsers.FirstOrDefault(candidate => candidate.SessionId == sessionUserId);
        if (user is null)
        {
            return;
        }

        var wasPrimary = user.IsPrimary;
        SignedInUsers.Remove(user);

        foreach (var assignment in ControllerAssignments.Where(item => item.SessionUserId == sessionUserId).ToArray())
        {
            ControllerAssignments.Remove(assignment);
        }

        if (wasPrimary && SignedInUsers.Count > 0)
        {
            SignedInUsers[0].IsPrimary = true;
        }

        RaiseChanged();
    }

    public void SignOutAll()
    {
        SignedInUsers.Clear();
        ControllerAssignments.Clear();
        RaiseChanged();
    }

    private void AssignControllerInternal(int controllerIndex, Guid sessionUserId)
    {
        if (controllerIndex is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(controllerIndex));
        }

        foreach (var existing in ControllerAssignments.Where(item => item.ControllerIndex == controllerIndex).ToArray())
        {
            ControllerAssignments.Remove(existing);
        }

        ControllerAssignments.Add(new ControllerAssignment(controllerIndex, sessionUserId));
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
