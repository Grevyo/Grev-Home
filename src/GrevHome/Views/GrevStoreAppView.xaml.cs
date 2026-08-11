using System.Windows;
using System.Windows.Controls;
using GrevHome.Apps;
using GrevHome.Presentation;
using GrevHome.Sessions;
using GrevHome.Store;

namespace GrevHome.Views;

public partial class GrevStoreAppView : UserControl
{
    private GrevStorePackageDefinition? _package;
    private InstalledAppEntry? _installedEntry;

    public event Action<GrevStorePackageDefinition>? DownloadRequested;
    public event Action<InstalledAppEntry>? OpenRequested;
    public event Action<GrevStorePackageDefinition>? UninstallRequested;

    public GrevStoreAppView()
    {
        InitializeComponent();
    }

    public void SetPackage(
        GrevStorePackageDefinition package,
        SessionUser? primaryUser,
        InstalledAppEntry? installedEntry,
        string installLocation)
    {
        _package = package;
        _installedEntry = installedEntry;

        AppArtworkHost.Width = DefaultThemeMetrics.AppTileWidth;
        AppArtworkHost.Height = DefaultThemeMetrics.AppTileHeight;
        AppArtworkHost.Child = AppArtworkFactory.Create(
            package.Presentation.IconAsset,
            package.Presentation.TileColor,
            DefaultThemeMetrics.AppTileWidth,
            DefaultThemeMetrics.AppTileHeight,
            20);
        AppNameText.Text = package.Presentation.DisplayName;
        AppTypeText.Text = $"{FormatCategory(package.Category)}  •  {package.App.Kind}";
        CategoryText.Text = FormatCategory(package.Category);
        ControllerSupportText.Text = package.App.SupportsController ? "Supported" : "Not declared";
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
            : "One machine-wide installation available to eligible profiles";

        DescriptionText.Text = package.StoreDescription ?? package.App.Description ?? "No Store description is available yet.";
        OwnershipDetailText.Text = package.IsProfileInstall
            ? hasPersistentPrimary
                ? $"This package is owned by the current Primary GrevID. Installing it for {primaryUser!.DisplayName} does not install it for any other profile. Its app files and profile data remain isolated from other GrevIDs."
                : "This package is profile-owned. Select a persistent local profile as Primary before downloading it."
            : "This package installs once for the machine. Per-user data behavior is still controlled separately by the package's App Data strategy.";
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

        var installed = installedEntry is not null;
        DownloadButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        OpenButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        UninstallButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.IsEnabled = !package.IsProfileInstall || hasPersistentPrimary;
        OpenButton.IsEnabled = installedEntry?.AvailableToCurrentUser == true;
        UninstallButton.IsEnabled = installed;

        if (installedEntry is null)
        {
            InstallationStateText.Text = package.IsProfileInstall && hasPersistentPrimary
                ? $"Not installed for {primaryUser!.DisplayName}"
                : "Not installed";
            InstallationMetadataText.Text = package.IsProfileInstall
                ? "Download will create a separate installation for the current Primary GrevID."
                : "Download will create the package's machine-wide installation.";
            StatusText.Text = DownloadButton.IsEnabled
                ? "Ready to download."
                : "Choose a persistent local Primary User before downloading this Profile App.";
        }
        else
        {
            var manifest = installedEntry.Manifest;
            InstallationStateText.Text = $"Installed • v{manifest.Version}";
            InstallationMetadataText.Text = $"Installed {manifest.InstalledAtUtc.ToLocalTime():g}" +
                                               (string.IsNullOrWhiteSpace(manifest.OwnerGrevId) ? string.Empty : $"  •  Owner {manifest.OwnerGrevId}");
            StatusText.Text = installedEntry.AvailableToCurrentUser
                ? "Open launches this installed app through the Grev Home runtime."
                : installedEntry.AvailabilityMessage ?? "This installation is not available to the current user.";
        }
    }

    public void SetBusy(string action, string message, double? progressPercent = null)
    {
        DownloadButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
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

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_package is not null) UninstallRequested?.Invoke(_package);
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
