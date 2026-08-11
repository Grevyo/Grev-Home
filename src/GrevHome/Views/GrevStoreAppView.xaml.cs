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
        InstalledAppEntry? installedEntry,
        string installLocation,
        bool isInCurrentUserLibrary = true)
    {
        _package = package;
        _installedEntry = installedEntry;
        _uninstallArmed = false;

        AppArtworkHost.Width = DefaultThemeMetrics.AppTileWidth;
        AppArtworkHost.Height = DefaultThemeMetrics.AppTileHeight;
        AppArtworkHost.Child = AppArtworkFactory.CreateTile(
            package.Presentation.DisplayName,
            package.Presentation.IconAsset,
            package.Presentation.TileColor);
        AppNameText.Text = package.Presentation.DisplayName;
        AppTypeText.Text = $"{FormatCategory(package.Category)}  •  {package.App.Kind}";
        CategoryText.Text = FormatCategory(package.Category);
        var hasGrevControllerDefaults = package.ControllerProfile?.Mappings?.Any(mapping =>
            mapping.Output.Kind != AppControllerOutputKind.None) == true;
        ControllerSupportText.Text = hasGrevControllerDefaults
            ? "Grev Enhanced"
            : package.App.SupportsController
                ? "Native • Grev optional"
                : "Grev profile available";
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
            : "One machine installation • each GrevID chooses whether it appears in their library";

        DescriptionText.Text = package.StoreDescription ?? package.App.Description ?? "No Store description is available yet.";
        OwnershipDetailText.Text = package.IsProfileInstall
            ? hasPersistentPrimary
                ? $"This package is owned by the current Primary GrevID. Installing it for {primaryUser!.DisplayName} does not install it for any other profile. Its app files and profile data remain isolated from other GrevIDs."
                : "This package is profile-owned. Select a persistent local profile as Primary before downloading it."
            : "This package is installed once for the Windows machine. Removing it from a normal GrevID only removes it from that user's Grev Home library. A real machine-wide uninstall is restricted to the Admin Console.";
        InstallPathText.Text = $"Install location: {installLocation}";

        IntegrationsPanel.Children.Clear();
        var integrations = package.GrevHomeIntegrations ?? Array.Empty<string>();
        foreach (var integration in integrations)
        {
            var card = new Border
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
            };
            IntegrationsPanel.Children.Add(card);
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

        var machineInstalled = installedEntry is not null;
        var availableInLibrary = package.IsProfileInstall
            ? machineInstalled
            : machineInstalled && isInCurrentUserLibrary;
        var canRemoveFromLibrary = !package.IsProfileInstall && machineInstalled && hasPersistentPrimary && isInCurrentUserLibrary;

        DownloadButton.Content = !package.IsProfileInstall && machineInstalled && !isInCurrentUserLibrary
            ? "Add to Library"
            : "Download";
        UninstallButton.Content = package.IsProfileInstall ? "Uninstall" : "Remove from Library";

        DownloadButton.Visibility = !machineInstalled || (!package.IsProfileInstall && !isInCurrentUserLibrary)
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpenButton.Visibility = availableInLibrary ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = availableInLibrary ? Visibility.Visible : Visibility.Collapsed;
        UninstallButton.Visibility = package.IsProfileInstall
            ? machineInstalled ? Visibility.Visible : Visibility.Collapsed
            : canRemoveFromLibrary ? Visibility.Visible : Visibility.Collapsed;

        DownloadButton.IsEnabled = (!package.IsProfileInstall || hasPersistentPrimary) &&
                                   (!machineInstalled || !package.IsProfileInstall);
        OpenButton.IsEnabled = availableInLibrary && installedEntry?.AvailableToCurrentUser == true;
        SettingsButton.IsEnabled = availableInLibrary && installedEntry?.AvailableToCurrentUser == true;
        UninstallButton.IsEnabled = package.IsProfileInstall ? machineInstalled : canRemoveFromLibrary;

        if (installedEntry is null)
        {
            InstallationStateText.Text = package.IsProfileInstall && hasPersistentPrimary
                ? $"Not installed for {primaryUser!.DisplayName}"
                : "Not installed";
            InstallationMetadataText.Text = package.IsProfileInstall
                ? "Download will create a separate installation for the current Primary GrevID."
                : "Download will create the package's machine-wide installation and add it to this user's library.";
            StatusText.Text = DownloadButton.IsEnabled
                ? "Ready to download."
                : "Choose a persistent local Primary User before downloading this Profile App.";
            return;
        }

        var manifest = installedEntry.Manifest;
        if (!package.IsProfileInstall && !isInCurrentUserLibrary)
        {
            InstallationStateText.Text = $"Installed on machine • v{manifest.Version} • not in your library";
            InstallationMetadataText.Text = $"Machine installation registered {manifest.InstalledAtUtc.ToLocalTime():g}.";
            StatusText.Text = hasPersistentPrimary
                ? "Add to Library makes this existing machine installation available to the current GrevID without downloading it again."
                : "A persistent local Primary User is required to save library membership.";
            return;
        }

        InstallationStateText.Text = $"Installed • v{manifest.Version}";
        InstallationMetadataText.Text = $"Installed {manifest.InstalledAtUtc.ToLocalTime():g}" +
                                           (string.IsNullOrWhiteSpace(manifest.OwnerGrevId) ? string.Empty : $"  •  Owner {manifest.OwnerGrevId}");
        StatusText.Text = installedEntry.AvailableToCurrentUser
            ? package.IsProfileInstall
                ? "Open launches through Grev Home. App Settings contains the standardized per-app controller profile for this GrevID."
                : "Open launches through Grev Home. Remove from Library affects only this GrevID; machine uninstall is Admin Console only."
            : installedEntry.AvailabilityMessage ?? "This installation is not available to the current user.";
    }

    public void SetBusy(string action, string message, double? progressPercent = null)
    {
        DownloadButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
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
                StatusText.Text = "Press Confirm Uninstall again to continue to the final data-loss warning. Nothing has been removed yet.";
            }
            else
            {
                UninstallButton.Content = "Confirm Remove from Library";
                StatusText.Text = "Press again to remove this Global App from the current GrevID's library. The app will remain installed on the machine.";
            }
            return;
        }

        _uninstallArmed = false;
        UninstallButton.Content = _package.IsProfileInstall ? "Uninstall" : "Remove from Library";
        UninstallRequested?.Invoke(_package);
    }

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
