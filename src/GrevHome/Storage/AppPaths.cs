using System.IO;

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

    public string GetProfileRoot(string grevId) =>
        Path.Combine(Profiles, ValidateGrevId(grevId));

    public string GetProfileMetadata(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "profile.json");

    public string GetProfileApps(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Apps");

    public string GetProfileAppData(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "AppData");

    public string GetProfileSaves(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Saves");

    public string GetProfileStats(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Stats");

    public string GetProfileConnections(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Connections");

    public string GetProfileScreenshots(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Screenshots");

    public string GetProfileThemes(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Themes");

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

    public void EnsureProfileLayout(string grevId)
    {
        Directory.CreateDirectory(GetProfileRoot(grevId));
        Directory.CreateDirectory(GetProfileApps(grevId));
        Directory.CreateDirectory(GetProfileAppData(grevId));
        Directory.CreateDirectory(GetProfileSaves(grevId));
        Directory.CreateDirectory(GetProfileStats(grevId));
        Directory.CreateDirectory(GetProfileConnections(grevId));
        Directory.CreateDirectory(GetProfileScreenshots(grevId));
        Directory.CreateDirectory(GetProfileThemes(grevId));
    }

    public void EnsureGuestLayout()
    {
        Directory.CreateDirectory(GuestShared);
        Directory.CreateDirectory(Path.Combine(GuestShared, "AppData"));
        Directory.CreateDirectory(Path.Combine(GuestShared, "Saves"));
        Directory.CreateDirectory(Path.Combine(GuestShared, "Stats"));
        Directory.CreateDirectory(Path.Combine(GuestShared, "Connections"));
    }

    private static string ValidateGrevId(string grevId)
    {
        if (string.IsNullOrWhiteSpace(grevId) ||
            grevId.Length > 58 ||
            grevId[0] != 'G' ||
            grevId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("Invalid GrevId.", nameof(grevId));
        }

        return grevId;
    }
}
