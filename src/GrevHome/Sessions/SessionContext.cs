using System.Collections.ObjectModel;

namespace GrevHome.Sessions;

public enum AccountKind
{
    Local,
    Guest,
    GrevDad
}

public sealed record SessionUser(
    Guid Id,
    string DisplayName,
    AccountKind AccountKind,
    bool IsPrimary);

public sealed class SessionContext
{
    public ObservableCollection<SessionUser> SignedInUsers { get; } = new();

    public SessionUser? PrimaryUser => SignedInUsers.FirstOrDefault(user => user.IsPrimary);

    public event EventHandler? Changed;

    public void SignInSinglePrimary(string displayName, AccountKind accountKind)
    {
        SignedInUsers.Clear();
        SignedInUsers.Add(new SessionUser(Guid.NewGuid(), displayName, accountKind, IsPrimary: true));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOutAll()
    {
        SignedInUsers.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
