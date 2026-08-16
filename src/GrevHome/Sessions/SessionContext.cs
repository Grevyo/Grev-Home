using System.Collections.ObjectModel;
using GrevHome.Profiles;

namespace GrevHome.Sessions;

public enum AccountKind
{
    Local,
    Guest
}

public sealed class SessionUser
{
    public Guid SessionId { get; } = Guid.NewGuid();
    public string? GrevId { get; }
    public string? Username { get; }
    public string DisplayName { get; internal set; }
    public AccountKind AccountKind { get; }
    public AccountRole Role { get; internal set; }
    public bool IsPrimary { get; internal set; }

    public SessionUser(string? grevId, string? username, string displayName, AccountKind accountKind, AccountRole role)
    {
        GrevId = grevId;
        Username = username;
        DisplayName = displayName;
        AccountKind = accountKind;
        Role = role;
    }
}

public sealed record ControllerAssignment(int ControllerIndex, Guid SessionUserId);

public sealed class SessionContext
{
    private const int MaximumPlayers = 4;

    public ObservableCollection<SessionUser> SignedInUsers { get; } = new();
    public ObservableCollection<ControllerAssignment> ControllerAssignments { get; } = new();
    public SessionUser? PrimaryUser => SignedInUsers.FirstOrDefault(user => user.IsPrimary);
    public bool HasSignedInUsers => SignedInUsers.Count > 0;
    public event EventHandler? Changed;

    public SessionUser SignInLocal(LocalProfile profile, int? controllerIndex = null)
    {
        var user = SignedInUsers.FirstOrDefault(candidate => string.Equals(candidate.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase));

        if (user is null && SignedInUsers.Count >= MaximumPlayers)
        {
            throw new InvalidOperationException($"Grev Home supports up to {MaximumPlayers} signed-in players.");
        }

        if (controllerIndex.HasValue)
        {
            EnsureControllerAvailableForJoin(controllerIndex.Value, user?.SessionId);
        }

        if (user is null)
        {
            user = new SessionUser(profile.GrevId, profile.Username, profile.DisplayName, AccountKind.Local, profile.Role)
            {
                IsPrimary = SignedInUsers.Count == 0
            };
            SignedInUsers.Add(user);
        }
        else
        {
            user.DisplayName = profile.DisplayName;
            user.Role = profile.Role;
        }

        if (controllerIndex.HasValue) AssignControllerInternal(controllerIndex.Value, user.SessionId);
        RaiseChanged();
        return user;
    }

    public SessionUser SignInGuest(int? controllerIndex = null)
    {
        if (SignedInUsers.All(user => user.AccountKind != AccountKind.Local))
        {
            throw new InvalidOperationException("A temporary Guest must join an existing local-player session.");
        }

        if (SignedInUsers.Count >= MaximumPlayers)
        {
            throw new InvalidOperationException($"Grev Home supports up to {MaximumPlayers} signed-in players.");
        }

        if (controllerIndex.HasValue)
        {
            EnsureControllerAvailableForJoin(controllerIndex.Value);
        }

        var usedGuestNumbers = SignedInUsers
            .Where(user => user.AccountKind == AccountKind.Guest)
            .Select(user => ParseGuestNumber(user.DisplayName))
            .Where(number => number > 0)
            .ToHashSet();
        var guestNumber = Enumerable.Range(1, MaximumPlayers).First(number => !usedGuestNumbers.Contains(number));
        var guest = new SessionUser(null, null, $"Guest {guestNumber}", AccountKind.Guest, AccountRole.Guest);
        SignedInUsers.Add(guest);

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

        // Temporary Guests have no GrevID and therefore cannot own the active profile/app context.
        // A persistent local profile whose role is Guest is still AccountKind.Local and may be Primary.
        if (requested.AccountKind == AccountKind.Guest)
        {
            return;
        }

        foreach (var user in SignedInUsers) user.IsPrimary = user.SessionId == requested.SessionId;
        RaiseChanged();
    }

    public void UpdateDisplayName(string grevId, string displayName)
    {
        var user = FindLocalUser(grevId);
        if (user is null) return;
        user.DisplayName = displayName;
        RaiseChanged();
    }

    public void UpdateRole(string grevId, AccountRole role)
    {
        var user = FindLocalUser(grevId);
        if (user is null) return;
        user.Role = role;
        RaiseChanged();
    }

    public void AssignController(int controllerIndex, Guid sessionUserId)
    {
        if (SignedInUsers.All(user => user.SessionId != sessionUserId)) throw new InvalidOperationException("That user is not signed in.");
        AssignControllerInternal(controllerIndex, sessionUserId);
        RaiseChanged();
    }

    public void UnassignController(int controllerIndex, Guid? expectedSessionUserId = null)
    {
        ValidateControllerIndex(controllerIndex);
        var assignments = ControllerAssignments
            .Where(item => item.ControllerIndex == controllerIndex && (!expectedSessionUserId.HasValue || item.SessionUserId == expectedSessionUserId.Value))
            .ToArray();
        if (assignments.Length == 0) return;
        foreach (var assignment in assignments) ControllerAssignments.Remove(assignment);
        RaiseChanged();
    }

    public SessionUser? GetUserForController(int controllerIndex)
    {
        ValidateControllerIndex(controllerIndex);
        var assignment = ControllerAssignments.FirstOrDefault(item => item.ControllerIndex == controllerIndex);
        return assignment is null ? null : SignedInUsers.FirstOrDefault(user => user.SessionId == assignment.SessionUserId);
    }

    public IReadOnlyList<int> GetControllersForUser(Guid sessionUserId) =>
        ControllerAssignments.Where(item => item.SessionUserId == sessionUserId).Select(item => item.ControllerIndex).OrderBy(index => index).ToArray();

    public void SignOut(Guid sessionUserId)
    {
        var user = SignedInUsers.FirstOrDefault(candidate => candidate.SessionId == sessionUserId);
        if (user is null) return;
        var wasPrimary = user.IsPrimary;
        SignedInUsers.Remove(user);
        foreach (var assignment in ControllerAssignments.Where(item => item.SessionUserId == sessionUserId).ToArray()) ControllerAssignments.Remove(assignment);

        var remainingLocalUsers = SignedInUsers.Where(candidate => candidate.AccountKind == AccountKind.Local).ToArray();
        if (remainingLocalUsers.Length == 0)
        {
            // Temporary Guests belong to a hosted local session. They never survive after the final
            // persistent local account leaves because there would be no valid Primary/GrevID owner.
            SignedInUsers.Clear();
            ControllerAssignments.Clear();
            RaiseChanged();
            return;
        }

        if (wasPrimary)
        {
            foreach (var remaining in SignedInUsers) remaining.IsPrimary = false;
            remainingLocalUsers[0].IsPrimary = true;
        }

        RaiseChanged();
    }

    public void SignOutAll()
    {
        SignedInUsers.Clear();
        ControllerAssignments.Clear();
        RaiseChanged();
    }

    private SessionUser? FindLocalUser(string grevId) =>
        SignedInUsers.FirstOrDefault(candidate => string.Equals(candidate.GrevId, grevId, StringComparison.OrdinalIgnoreCase));

    private void EnsureControllerAvailableForJoin(int controllerIndex, Guid? joiningSessionUserId = null)
    {
        ValidateControllerIndex(controllerIndex);
        var currentOwner = GetUserForController(controllerIndex);
        if (currentOwner is not null && currentOwner.SessionId != joiningSessionUserId)
        {
            throw new InvalidOperationException(
                $"Controller {controllerIndex + 1} is already assigned to {currentOwner.DisplayName}. Use an unassigned controller to join.");
        }
    }

    private void AssignControllerInternal(int controllerIndex, Guid sessionUserId)
    {
        ValidateControllerIndex(controllerIndex);
        foreach (var existing in ControllerAssignments.Where(item => item.ControllerIndex == controllerIndex).ToArray()) ControllerAssignments.Remove(existing);
        ControllerAssignments.Add(new ControllerAssignment(controllerIndex, sessionUserId));
    }

    private static void ValidateControllerIndex(int controllerIndex)
    {
        if (controllerIndex is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(controllerIndex));
    }

    private static int ParseGuestNumber(string displayName)
    {
        const string prefix = "Guest ";
        return displayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(displayName[prefix.Length..], out var number)
            ? number
            : 0;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
