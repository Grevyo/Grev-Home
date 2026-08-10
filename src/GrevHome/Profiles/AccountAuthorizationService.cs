namespace GrevHome.Profiles;

public enum AccountPermission
{
    LaunchApps,
    UseFiles,
    EditOwnProfile,
    ManageProfiles,
    ManageRoles,
    ManagePlayers,
    AssignControllers,
    ChangePrimaryUser,
    MachineSettings,
    SystemPower,
    InstallPackages,
    ManageConnections
}

public static class AccountAuthorizationService
{
    private static readonly IReadOnlySet<AccountPermission> AdminPermissions =
        Enum.GetValues<AccountPermission>().ToHashSet();

    private static readonly IReadOnlySet<AccountPermission> StandardPermissions = new HashSet<AccountPermission>
    {
        AccountPermission.LaunchApps,
        AccountPermission.UseFiles,
        AccountPermission.EditOwnProfile,
        AccountPermission.ManagePlayers,
        AccountPermission.AssignControllers,
        AccountPermission.ChangePrimaryUser,
        AccountPermission.ManageConnections
    };

    private static readonly IReadOnlySet<AccountPermission> GuestPermissions = new HashSet<AccountPermission>
    {
        AccountPermission.LaunchApps,
        AccountPermission.EditOwnProfile,
        AccountPermission.AssignControllers
    };

    public static bool Allows(AccountRole role, AccountPermission permission) =>
        GetPermissions(role).Contains(permission);

    public static IReadOnlySet<AccountPermission> GetPermissions(AccountRole role) => role switch
    {
        AccountRole.Admin => AdminPermissions,
        AccountRole.Standard => StandardPermissions,
        AccountRole.Guest => GuestPermissions,
        _ => GuestPermissions
    };

    public static bool CanEditProfile(AccountRole actorRole, string? actorGrevId, string targetGrevId)
    {
        var isOwnProfile = !string.IsNullOrWhiteSpace(actorGrevId) &&
                           string.Equals(actorGrevId, targetGrevId, StringComparison.OrdinalIgnoreCase);

        return isOwnProfile
            ? Allows(actorRole, AccountPermission.EditOwnProfile)
            : Allows(actorRole, AccountPermission.ManageProfiles);
    }

    public static string DescribeRole(AccountRole role) => role switch
    {
        AccountRole.Admin => "Full Grev Home administration, player management and machine controls.",
        AccountRole.Standard => "Normal player access, files and session/controller management without administrative account or machine changes.",
        AccountRole.Guest => "Restricted player access for launching apps, editing its own profile and using an assigned controller.",
        _ => role.ToString()
    };

    public static string SummarizePermissions(AccountRole role)
    {
        var labels = GetPermissions(role)
            .Select(FormatPermission)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase);
        return string.Join("  •  ", labels);
    }

    private static string FormatPermission(AccountPermission permission) => permission switch
    {
        AccountPermission.LaunchApps => "Launch apps",
        AccountPermission.UseFiles => "Files",
        AccountPermission.EditOwnProfile => "Edit own profile",
        AccountPermission.ManageProfiles => "Manage profiles",
        AccountPermission.ManageRoles => "Manage roles",
        AccountPermission.ManagePlayers => "Manage players",
        AccountPermission.AssignControllers => "Assign controllers",
        AccountPermission.ChangePrimaryUser => "Change Primary",
        AccountPermission.MachineSettings => "Machine settings",
        AccountPermission.SystemPower => "Power controls",
        AccountPermission.InstallPackages => "Install packages",
        AccountPermission.ManageConnections => "Account connections",
        _ => permission.ToString()
    };
}
