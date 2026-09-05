using System.IO;
using GrevHome.Apps;

namespace GrevHome.Storage;

public sealed class AppPaths
{
    private const int MaxGrevIdLength = 58;
    private const string DefaultRoot = @"C:\GrevHome";

    public string Root { get; }
    public string Data => Path.Combine(Root, "Data");
    public string AppCatalogueData => Path.Combine(Data, "Apps");
    public string AppCatalogueFile => Path.Combine(AppCatalogueData, "catalog.json");
    public string RuntimeData => Path.Combine(Data, "Runtime");
    public string InputData => Path.Combine(Data, "Input");
    public string ControllerShortcutsFile => Path.Combine(InputData, "controller-shortcuts.json");
    public string NotificationData => Path.Combine(Data, "Notifications");
    public string NotificationFile => Path.Combine(NotificationData, "notifications.json");
    public string TransferData => Path.Combine(Data, "Transfers");
    public string TransferStateFile => Path.Combine(TransferData, "transfers.json");
    public string PresentationData => Path.Combine(Data, "Presentation");
    public string ShellMotionSettingsFile => Path.Combine(PresentationData, "shell-motion.json");
    public string Profiles => Path.Combine(Root, "Profiles");
    public string GuestShared => Path.Combine(Profiles, "_GuestShared");
    public string GuestStats => Path.Combine(GuestShared, "Stats");
    public string GuestPlaytimeFile => Path.Combine(GuestStats, "playtime.json");
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
            ?? DefaultRoot;
    }

    public string GetProfileRoot(string grevId) =>
        Path.Combine(Profiles, ValidateGrevId(grevId));

    public string GetProfileMetadata(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "profile.json");

    public string GetProfileApps(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Apps");

    public string GetProfileAppRoot(string grevId, string appId) =>
        Path.Combine(GetProfileApps(grevId), AppIdentity.ValidateAppId(appId));

    public string GetProfileAppData(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "AppData");

    public string GetProfileAppDataRoot(string grevId, string appId) =>
        Path.Combine(GetProfileAppData(grevId), AppIdentity.ValidateAppId(appId));

    public string GetProfileSaves(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Saves");

    public string GetProfileStats(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Stats");

    public string GetProfilePlaytimeFile(string grevId) =>
        Path.Combine(GetProfileStats(grevId), "playtime.json");

    public string GetProfileConnections(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Connections");

    public string GetProfileScreenshots(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Screenshots");

    public string GetProfileThemes(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Themes");

    public string GetProfilePresentation(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Presentation");

    public string GetProfileAppPresentation(string grevId) =>
        Path.Combine(GetProfilePresentation(grevId), "Apps");

    public string GetProfileAppPresentationRoot(string grevId, string appId) =>
        Path.Combine(GetProfileAppPresentation(grevId), AppIdentity.ValidateAppId(appId));

    public string GetProfileAppPresentationMetadata(string grevId, string appId) =>
        Path.Combine(GetProfileAppPresentationRoot(grevId, appId), "presentation.json");

    public string GetProfileSettings(string grevId) =>
        Path.Combine(GetProfileRoot(grevId), "Settings");

    public string GetProfileAppLibraryPreferencesFile(string grevId) =>
        Path.Combine(GetProfileSettings(grevId), "app-library.json");

    public string GetProfileAppSettings(string grevId) =>
        Path.Combine(GetProfileSettings(grevId), "Apps");

    public string GetProfileAppSettingsRoot(string grevId, string appId) =>
        Path.Combine(GetProfileAppSettings(grevId), AppIdentity.ValidateAppId(appId));

    public string GetProfileAppControllerProfileFile(string grevId, string appId) =>
        Path.Combine(GetProfileAppSettingsRoot(grevId, appId), "controller-profile.json");

    public string GetProfileAppControllerGuidePreferenceFile(string grevId, string appId) =>
        Path.Combine(GetProfileAppSettingsRoot(grevId, appId), "controller-guide.json");

    public string GetGlobalAppRoot(string appId) =>
        Path.Combine(GlobalApps, AppIdentity.ValidateAppId(appId));

    public string GetGlobalAppDataRoot(string appId) =>
        Path.Combine(GlobalAppData, AppIdentity.ValidateAppId(appId));

    public void EnsureMachineLayout()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(AppCatalogueData);
        Directory.CreateDirectory(RuntimeData);
        Directory.CreateDirectory(InputData);
        Directory.CreateDirectory(NotificationData);
        Directory.CreateDirectory(TransferData);
        Directory.CreateDirectory(PresentationData);
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
        Directory.CreateDirectory(GetProfilePresentation(grevId));
        Directory.CreateDirectory(GetProfileAppPresentation(grevId));
        Directory.CreateDirectory(GetProfileSettings(grevId));
        Directory.CreateDirectory(GetProfileAppSettings(grevId));
    }

    public void EnsureGuestLayout()
    {
        Directory.CreateDirectory(GuestShared);
        Directory.CreateDirectory(Path.Combine(GuestShared, "AppData"));
        Directory.CreateDirectory(Path.Combine(GuestShared, "Saves"));
        Directory.CreateDirectory(GuestStats);
        Directory.CreateDirectory(Path.Combine(GuestShared, "Connections"));
    }

    private static string ValidateGrevId(string grevId)
    {
        if (string.IsNullOrWhiteSpace(grevId) ||
            grevId.Length > MaxGrevIdLength ||
            grevId[0] != 'G' ||
            grevId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("Invalid GrevId.", nameof(grevId));
        }

        return grevId;
    }
}
