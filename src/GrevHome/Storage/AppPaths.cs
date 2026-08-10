namespace GrevHome.Storage;

public sealed class AppPaths
{
    public string Root { get; }
    public string Data => Path.Combine(Root, "Data");
    public string Profiles => Path.Combine(Root, "Profiles");
    public string GuestShared => Path.Combine(Profiles, "_GuestShared");
    public string Global => Path.Combine(Root, "Global");
    public string GlobalApps => Path.Combine(Global, "Apps");
    public string GlobalAppData => Path.Combine(Global, "AppData");
    public string Packages => Path.Combine(Root, "Packages");
    public string Themes => Path.Combine(Root, "Themes");
    public string Downloads => Path.Combine(Root, "Downloads");
    public string Logs => Path.Combine(Root, "Logs");

    public AppPaths(string? root = null)
    {
        Root = root
            ?? Environment.GetEnvironmentVariable("GREV_HOME_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Grev Home");
    }

    public string GetProfileRoot(Guid profileId) =>
        Path.Combine(Profiles, profileId.ToString("N"));

    public string GetProfileMetadata(Guid profileId) =>
        Path.Combine(GetProfileRoot(profileId), "profile.json");

    public string GetProfileApps(Guid profileId) =>
        Path.Combine(GetProfileRoot(profileId), "Apps");

    public string GetProfileAppData(Guid profileId) =>
        Path.Combine(GetProfileRoot(profileId), "AppData");

    public string GetProfileSaves(Guid profileId) =>
        Path.Combine(GetProfileRoot(profileId), "Saves");

    public string GetProfileStats(Guid profileId) =>
        Path.Combine(GetProfileRoot(profileId), "Stats");

    public string GetProfileConnections(Guid profileId) =>
        Path.Combine(GetProfileRoot(profileId), "Connections");

    public string GetProfileScreenshots(Guid profileId) =>
        Path.Combine(GetProfileRoot(profileId), "Screenshots");

    public string GetProfileThemes(Guid profileId) =>
        Path.Combine(GetProfileRoot(profileId), "Themes");

    public void EnsureMachineLayout()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Profiles);
        Directory.CreateDirectory(Global);
        Directory.CreateDirectory(GlobalApps);
        Directory.CreateDirectory(GlobalAppData);
        Directory.CreateDirectory(Packages);
        Directory.CreateDirectory(Themes);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(Logs);
        EnsureGuestLayout();
    }

    public void EnsureProfileLayout(Guid profileId)
    {
        Directory.CreateDirectory(GetProfileRoot(profileId));
        Directory.CreateDirectory(GetProfileApps(profileId));
        Directory.CreateDirectory(GetProfileAppData(profileId));
        Directory.CreateDirectory(GetProfileSaves(profileId));
        Directory.CreateDirectory(GetProfileStats(profileId));
        Directory.CreateDirectory(GetProfileConnections(profileId));
        Directory.CreateDirectory(GetProfileScreenshots(profileId));
        Directory.CreateDirectory(GetProfileThemes(profileId));
    }

    public void EnsureGuestLayout()
    {
        Directory.CreateDirectory(GuestShared);
        Directory.CreateDirectory(Path.Combine(GuestShared, "AppData"));
        Directory.CreateDirectory(Path.Combine(GuestShared, "Saves"));
        Directory.CreateDirectory(Path.Combine(GuestShared, "Stats"));
        Directory.CreateDirectory(Path.Combine(GuestShared, "Connections"));
    }
}
