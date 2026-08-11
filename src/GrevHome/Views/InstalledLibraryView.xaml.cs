using System.Windows;
using System.Windows.Controls;
using GrevHome.Apps;
using GrevHome.Presentation;
using GrevHome.Runtime;
using GrevHome.Sessions;
using GrevHome.Store;

namespace GrevHome.Views;

public partial class InstalledLibraryView : UserControl
{
    private readonly GrevStoreCatalogService _storeCatalog = new();
    private IReadOnlyList<InstalledAppEntry> _entries = Array.Empty<InstalledAppEntry>();
    private string _filter = "All";
    private SessionUser? _primaryUser;

    public event EventHandler? BackRequested;
    public event Action<InstalledAppEntry>? LaunchRequested;
    public event Action<InstalledAppEntry>? SettingsRequested;

    public InstalledLibraryView()
    {
        InitializeComponent();
    }

    public void SetLibrary(IReadOnlyList<InstalledAppEntry> entries, SessionUser? primaryUser)
    {
        _entries = entries;
        _primaryUser = primaryUser;
        _filter = "All";

        ContextText.Text = primaryUser is null
            ? "No primary user."
            : primaryUser.GrevId is null
                ? $"{primaryUser.DisplayName} • Guest • shared apps only"
                : $"{primaryUser.DisplayName} • {primaryUser.GrevId} • shared + GrevID-local apps";

        Render();
    }

    public void ShowLaunchStarted(LaunchSessionSnapshot session)
    {
        StatusText.Text = $"Started {session.AppName} • session {session.LaunchSessionId.ToString()[..8]} • PID {session.RootProcessId}. Grev Home is staying resident in the background.";
    }

    public void ShowLaunchError(string message)
    {
        StatusText.Text = $"Launch failed: {message}";
    }

    private void Render()
    {
        AppsPanel.Children.Clear();

        var visible = _entries.Where(MatchesFilter).ToArray();
        EmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _entries.Count == 0
            ? "Nothing is installed yet. Install supported packages from Grev Store."
            : "No installed apps match this filter.";

        foreach (var entry in visible)
        {
            var definition = entry.Manifest.Definition;
            var package = _storeCatalog.Find(definition.AppId);
            var displayName = package?.Presentation.DisplayName ?? definition.Name;
            var tileColor = package?.Presentation.TileColor ?? "#151923";
            var icon = package?.Presentation.IconAsset;

            var appCard = new StackPanel
            {
                Width = DefaultThemeMetrics.AppTileWidth,
                Margin = new Thickness(8)
            };

            var launchButton = new Button
            {
                Width = DefaultThemeMetrics.AppTileWidth,
                Height = DefaultThemeMetrics.AppTileHeight,
                Padding = new Thickness(0),
                Tag = entry,
                IsEnabled = entry.AvailableToCurrentUser,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = AppArtworkFactory.CreateTile(displayName, icon, tileColor)
            };
            launchButton.Click += App_Click;

            var settingsButton = new Button
            {
                Width = DefaultThemeMetrics.AppTileWidth,
                Height = 42,
                Margin = new Thickness(0, 7, 0, 0),
                Padding = new Thickness(10, 6, 10, 6),
                Tag = entry,
                IsEnabled = entry.AvailableToCurrentUser,
                Content = "App Settings",
                FontSize = 13
            };
            settingsButton.Click += Settings_Click;

            appCard.Children.Add(launchButton);
            appCard.Children.Add(settingsButton);
            AppsPanel.Children.Add(appCard);
        }

        StatusText.Text = _entries.Count == 0
            ? "The Installed Library is ready for packages installed from Grev Store."
            : $"{visible.Length} shown • {_entries.Count} installed for this session context. Select an app to launch it, or open App Settings for its Grev controller profile.";
    }

    private bool MatchesFilter(InstalledAppEntry entry)
    {
        var kind = entry.Manifest.Definition.Kind;
        return _filter switch
        {
            "All" => true,
            "Application" => kind is AppKind.Application or AppKind.GameLauncher or AppKind.Media,
            "Emulator" => kind == AppKind.Emulator,
            "Utility" => kind is AppKind.Utility or AppKind.SystemTool,
            _ => true
        };
    }

    private void App_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InstalledAppEntry entry })
        {
            StatusText.Text = $"Starting {entry.Manifest.Definition.Name}...";
            LaunchRequested?.Invoke(entry);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InstalledAppEntry entry })
        {
            SettingsRequested?.Invoke(entry);
        }
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string filter })
        {
            _filter = filter;
            Render();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);
}
