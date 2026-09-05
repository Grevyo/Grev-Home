using System.Windows;
using System.Windows.Controls;
using GrevHome.Apps;
using GrevHome.Input;
using GrevHome.Presentation;
using GrevHome.Sessions;
using GrevHome.Store;

namespace GrevHome.Views;

public partial class GrevStoreAppView : UserControl
{
    private GrevStorePackageDefinition? _package;
    private InstalledAppEntry? _installedEntry;
    private bool _uninstallArmed;

    public event Action<GrevStorePackageDefinition>? DownloadRequested;
    public event Action<GrevStorePackageDefinition>? UpdateRequested;
    public event Action<GrevStorePackageDefinition>? RepairRequested;
    public event Action<InstalledAppEntry>? OpenRequested;
    public event Action<InstalledAppEntry>? SettingsRequested;
    public event Action<GrevStorePackageDefinition>? UninstallRequested;

    public GrevStoreAppView()
    {
        InitializeComponent();
    }

    public void SetPackage(
        GrevStorePackageDefinition package,
        SessionUser? primaryUser,
        AppLifecycleSnapshot lifecycle,
        string installLocation,
        string dataLocation)
    {
        _package = package;
        _installedEntry = lifecycle.InstalledEntry;
        _uninstallArmed = false;

        AppArtworkHost.Width = DefaultThemeMetrics.AppTileWidth;
        AppArtworkHost.Height = DefaultThemeMetrics.AppTileHeight;
        AppArtworkHost.Child = AppArtworkFactory.CreateTile(
            package.Presentation.DisplayName,
            package.Presentation.IconAsset,
            package.Presentation.TileColor);
        AppNameText.Text = package.Presentation.DisplayName;
        AppTypeText.Text = $"{FormatCategory(package.Category)}  •  {package.App.Kind}";
        LifecycleText.Text = FormatLifecycle(lifecycle.State);

        var hasGrevControllerDefaults = package.ControllerProfile?.Mappings?.Any(mapping =>
            mapping.Output.Kind != AppControllerOutputKind.None) == true;
        ControllerSupportText.Text = hasGrevControllerDefaults
            ? "Grev Enhanced"
            : package.App.SupportsController
                ? "Native • Grev optional"
                : package.Supports(AppPackageCapability.ControllerProfile)
                    ? "Grev profile available"
                    : "Not declared";
        InstallTypeText.Text = package.IsProfileInstall ? "Profile App" : "Global App";
        DataTypeText.Text = package.App.DataStrategy switch
        {
            DataStrategy.GrevId => "Per profile",
            DataStrategy.Global => "Machine-wide",
            DataStrategy.NativeAccount => "App account",
            _ => "App managed"
        };

        var hasPersistentPrimary = !string.IsNullOrWhiteSpace(primaryUser?.GrevId);
        OwnershipSummaryText.Text = package.IsProfileInstall
            ? hasPersistentPrimary
                ? $"Installs only for {primaryUser!.DisplayName} • {primaryUser.GrevId}"
                : "Requires a persistent local Primary User"
            : "One machine installation • each GrevID has independent library membership";

        var description = package.StoreDescription ?? package.App.Description ?? "No Store description is available yet.";
        if (!string.IsNullOrWhiteSpace(package.SetupNotice))
        {
            var setupNotice = package.SetupNotice
                .Replace("{InstallLocation}", installLocation, StringComparison.Ordinal)
                .Replace("{DataLocation}", dataLocation, StringComparison.Ordinal);
            description = $"{description}\n\n{setupNotice}";
        }
        DescriptionText.Text = description;

        OwnershipDetailText.Text = package.IsProfileInstall
            ? hasPersistentPrimary
                ? $"This package is owned by the current Primary GrevID. Install, update, repair and uninstall are scoped to {primaryUser!.DisplayName}; profile data is never silently shared with another GrevID."
                : "This package is profile-owned. Select a persistent local profile as Primary before managing it."
            : "This package is installed once for the Windows machine. A normal GrevID can add or remove it from their own Grev Home library without uninstalling the Windows application. Machine uninstall remains Admin Console only.";
        InstallPathText.Text = package.App.DataStrategy == DataStrategy.NativeAccount
            ? $"Install location: {installLocation}\nApp data: managed by the native Windows/app account"
            : $"Install location: {installLocation}\nData location: {dataLocation}";

        RenderIntegrations(package);
        RenderActions(package, lifecycle, hasPersistentPrimary);
        RenderInstallationState(package, primaryUser, lifecycle, hasPersistentPrimary);
    }

    private void RenderIntegrations(GrevStorePackageDefinition package)
    {
        IntegrationsPanel.Children.Clear();
        var integrations = package.GrevHomeIntegrations ?? Array.Empty<string>();
        foreach (var integration in integrations)
        {
            IntegrationsPanel.Children.Add(new Border
            {
                Margin = new Thickness(0, 5, 0, 0),
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(9),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(9, 12, 18)),
                Child = new TextBlock
                {
                    Text = $"•  {integration}",
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(215, 220, 231))
                }
            });
        }

        if (integrations.Count == 0)
        {
            IntegrationsPanel.Children.Add(new TextBlock
            {
                Text = "No Grev Home integrations have been declared for this package yet.",
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private void RenderActions(
        GrevStorePackageDefinition package,
        AppLifecycleSnapshot lifecycle,
        bool hasPersistentPrimary)
    {
        var machineInstalled = lifecycle.IsInstalled;
        var availableInLibrary = lifecycle.IsInCurrentUserLibrary && machineInstalled;
        var canAddToLibrary = !package.IsProfileInstall &&
                              machineInstalled &&
                              !lifecycle.IsInCurrentUserLibrary &&
                              package.Supports(AppPackageCapability.LibraryMembership) &&
                              hasPersistentPrimary;
        var canRemoveFromLibrary = !package.IsProfileInstall &&
                                   machineInstalled &&
                                   lifecycle.IsInCurrentUserLibrary &&
                                   package.Supports(AppPackageCapability.LibraryMembership) &&
                                   hasPersistentPrimary;

        DownloadButton.Content = canAddToLibrary ? "Add to Library" : "Download";
        UninstallButton.Content = package.IsProfileInstall ? "Uninstall" : "Remove from Library";

        DownloadButton.Visibility = (!machineInstalled && package.Supports(AppPackageCapability.Install)) || canAddToLibrary
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpenButton.Visibility = lifecycle.CanOpen ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton.Visibility = lifecycle.UpdateAvailable && package.Supports(AppPackageCapability.Update)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RepairButton.Visibility = machineInstalled && package.Supports(AppPackageCapability.Repair)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SettingsButton.Visibility = availableInLibrary && package.Supports(AppPackageCapability.AppSettings)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UninstallButton.Visibility = package.IsProfileInstall
            ? machineInstalled && package.Supports(AppPackageCapability.ProfileUninstall)
                ? Visibility.Visible
                : Visibility.Collapsed
            : canRemoveFromLibrary
                ? Visibility.Visible
                : Visibility.Collapsed;

        DownloadButton.IsEnabled = canAddToLibrary ||
                                   (!machineInstalled &&
                                    package.Supports(AppPackageCapability.Install) &&
                                    (!package.IsProfileInstall || hasPersistentPrimary));
        OpenButton.IsEnabled = lifecycle.CanOpen;
        UpdateButton.IsEnabled = lifecycle.UpdateAvailable && !lifecycle.IsRunning;
        RepairButton.IsEnabled = machineInstalled && !lifecycle.IsRunning;
        SettingsButton.IsEnabled = lifecycle.CanOpen;
        UninstallButton.IsEnabled = package.IsProfileInstall
            ? machineInstalled && !lifecycle.IsRunning
            : canRemoveFromLibrary;
    }

    private void RenderInstallationState(
        GrevStorePackageDefinition package,
        SessionUser? primaryUser,
        AppLifecycleSnapshot lifecycle,
        bool hasPersistentPrimary)
    {
        HealthText.Text = lifecycle.IsInstalled
            ? $"Health: {FormatHealth(lifecycle.Health.State)} • {lifecycle.Health.Message}" +
              (string.IsNullOrWhiteSpace(lifecycle.Health.DetectedVersion)
                  ? string.Empty
                  : $" • detected {lifecycle.Health.DetectedVersion}")
            : "Health inspection starts after the package is installed.";

        if (!lifecycle.IsInstalled)
        {
            InstallationStateText.Text = package.IsProfileInstall && hasPersistentPrimary
                ? $"Not installed for {primaryUser!.DisplayName}"
                : "Not installed";
            InstallationMetadataText.Text = package.IsProfileInstall
                ? "Download creates a separate installation for the current Primary GrevID."
                : "Download creates the package's machine installation and adds it to this user's library.";
            StatusText.Text = DownloadButton.IsEnabled
                ? "Ready to download."
                : "Choose a persistent local Primary User before managing this Profile App.";
            return;
        }

        var manifest = lifecycle.InstalledEntry!.Manifest;
        if (!package.IsProfileInstall && !lifecycle.IsInCurrentUserLibrary)
        {
            InstallationStateText.Text = $"Installed on machine • v{manifest.Version} • not in your library";
            InstallationMetadataText.Text = $"Machine installation registered {manifest.InstalledAtUtc.ToLocalTime():g}.";
            StatusText.Text = hasPersistentPrimary
                ? "Add to Library restores this existing machine installation for the current GrevID without downloading it again."
                : "A persistent local Primary User is required to save library membership.";
            return;
        }

        InstallationStateText.Text = lifecycle.State switch
        {
            AppLifecycleState.Running => $"Running • v{manifest.Version}",
            AppLifecycleState.UpdateAvailable => $"Update available • installed v{manifest.Version}",
            AppLifecycleState.RepairNeeded => $"Repair recommended • v{manifest.Version}",
            AppLifecycleState.Installing => "Installing…",
            AppLifecycleState.Updating => "Updating…",
            AppLifecycleState.Repairing => "Repairing…",
            AppLifecycleState.Uninstalling => "Uninstalling…",
            _ => $"Installed • v{manifest.Version}"
        };
        InstallationMetadataText.Text = $"Registered {manifest.InstalledAtUtc.ToLocalTime():g}" +
                                           (string.IsNullOrWhiteSpace(manifest.OwnerGrevId) ? string.Empty : $"  •  Owner {manifest.OwnerGrevId}");

        StatusText.Text = lifecycle.RepairNeeded
            ? "Repair is available because the package health check found missing or inconsistent app files."
            : lifecycle.UpdateAvailable
                ? $"This package declares {package.VersionPolicy?.CurrentVersion} as the trusted current Grev Home version. Close the app before updating."
                : package.VersionPolicy?.NativeAutoUpdate == true
                    ? "This app owns its native update lifecycle; Grev Home does not replace it with a second updater."
                    : package.IsProfileInstall
                        ? "Open launches through Grev Home. Update/repair preserve GrevID-owned data and App Settings remain per profile."
                        : "Open launches through Grev Home. Remove from Library affects only this GrevID; Admin Console owns machine-level actions.";
    }

    public void SetBusy(string action, string message, double? progressPercent = null)
    {
        DownloadButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
        UpdateButton.IsEnabled = false;
        RepairButton.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        UninstallButton.IsEnabled = false;
        StatusText.Text = progressPercent is null
            ? $"{action} • {message}"
            : $"{action} • {progressPercent.Value:0}% • {message}";
    }

    public void ShowStatus(string message) => StatusText.Text = message;

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_package is not null) DownloadRequested?.Invoke(_package);
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_package is not null) UpdateRequested?.Invoke(_package);
    }

    private void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (_package is not null) RepairRequested?.Invoke(_package);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_installedEntry is not null) OpenRequested?.Invoke(_installedEntry);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_installedEntry is not null) SettingsRequested?.Invoke(_installedEntry);
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_package is null) return;

        if (!_uninstallArmed)
        {
            _uninstallArmed = true;
            if (_package.IsProfileInstall)
            {
                UninstallButton.Content = "Confirm Uninstall";
                StatusText.Text = "Press Confirm Uninstall again to continue to the final profile-app uninstall warning. Nothing has been removed yet.";
            }
            else
            {
                UninstallButton.Content = "Confirm Remove from Library";
                StatusText.Text = "Press again to remove this Global App from the current GrevID's library. The Windows installation will remain unchanged.";
            }
            return;
        }

        _uninstallArmed = false;
        UninstallButton.Content = _package.IsProfileInstall ? "Uninstall" : "Remove from Library";
        UninstallRequested?.Invoke(_package);
    }

    private static string FormatLifecycle(AppLifecycleState state) => state switch
    {
        AppLifecycleState.NotInstalled => "Not installed",
        AppLifecycleState.Installed => "Installed",
        AppLifecycleState.RemovedFromLibrary => "Out of library",
        AppLifecycleState.Running => "Running",
        AppLifecycleState.UpdateAvailable => "Update available",
        AppLifecycleState.RepairNeeded => "Repair needed",
        AppLifecycleState.Installing => "Installing",
        AppLifecycleState.Updating => "Updating",
        AppLifecycleState.Repairing => "Repairing",
        AppLifecycleState.Uninstalling => "Uninstalling",
        _ => state.ToString()
    };

    private static string FormatHealth(PackageHealthState state) => state switch
    {
        PackageHealthState.Healthy => "Healthy",
        PackageHealthState.RepairRecommended => "Repair recommended",
        _ => "Unknown"
    };

    private static string FormatCategory(GrevStoreCategory category) => category switch
    {
        GrevStoreCategory.Gaming => "Gaming",
        GrevStoreCategory.Emulator => "Emulators",
        GrevStoreCategory.Application => "Apps",
        GrevStoreCategory.Media => "Media",
        GrevStoreCategory.Utility => "Tools",
        _ => category.ToString()
    };
}
