using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Apps;
using GrevHome.Store;

namespace GrevHome.Views;

public sealed record AdminMachineAppItem(
    InstalledAppEntry Entry,
    GrevStorePackageDefinition? Package,
    AppLifecycleSnapshot? Lifecycle,
    IReadOnlyList<string> LibraryUsers,
    bool CanUpdate,
    bool CanRepair,
    bool CanUninstall);

public partial class AdminConsoleView : UserControl
{
    private IReadOnlyList<AdminMachineAppItem> _items = Array.Empty<AdminMachineAppItem>();
    private string? _armedAppId;
    private bool _busy;

    public event EventHandler? BackRequested;
    public event Action<AdminMachineAppItem>? UpdateRequested;
    public event Action<AdminMachineAppItem>? RepairRequested;
    public event Action<AdminMachineAppItem>? UninstallRequested;

    public AdminConsoleView()
    {
        InitializeComponent();
    }

    public void SetApps(IReadOnlyList<AdminMachineAppItem> items)
    {
        _items = items;
        _armedAppId = null;
        RenderApps();
    }

    public void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText.Text = message;
        }
        RenderApps();
    }

    public void ShowStatus(string message) => StatusText.Text = message;

    private void RenderApps()
    {
        GlobalAppsPanel.Children.Clear();

        if (_items.Count == 0)
        {
            GlobalAppsPanel.Children.Add(new Border
            {
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(17, 21, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(43, 51, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Child = new TextBlock
                {
                    Text = "No Global Apps are registered as installed on this Grev Machine.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("MutedBrush")
                }
            });
            return;
        }

        foreach (var item in _items)
        {
            GlobalAppsPanel.Children.Add(CreateAppCard(item));
        }
    }

    private Border CreateAppCard(AdminMachineAppItem item)
    {
        var definition = item.Entry.Manifest.Definition;
        var lifecycle = item.Lifecycle;
        var card = new Border
        {
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 12),
            Background = new SolidColorBrush(Color.FromRgb(17, 21, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(43, 51, 68)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        var details = new StackPanel();
        details.Children.Add(new TextBlock
        {
            Text = item.Package?.Presentation.DisplayName ?? definition.Name,
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        details.Children.Add(CreateDetailText(
            $"Version {item.Entry.Manifest.Version}  •  {definition.InstallStrategy}  •  {definition.DataStrategy}"));
        details.Children.Add(CreateDetailText(
            definition.InstallStrategy == InstallStrategy.SystemInstalled
                ? $"Windows install: {Environment.ExpandEnvironmentVariables(definition.Launch.Executable)}"
                : $"Grev Home install: {item.Entry.BinaryRoot}"));

        if (lifecycle is not null)
        {
            details.Children.Add(CreateDetailText(
                $"Lifecycle: {FormatLifecycle(lifecycle.State)}  •  {(lifecycle.IsRunning ? "Running" : "Idle")}"));
            details.Children.Add(CreateDetailText(
                $"Health: {FormatHealth(lifecycle.Health.State)} • {lifecycle.Health.Message}"));
        }
        else
        {
            details.Children.Add(CreateDetailText(
                "Lifecycle: package metadata unavailable; destructive package actions are disabled."));
        }

        details.Children.Add(CreateDetailText(
            item.LibraryUsers.Count == 0
                ? "GrevID libraries: none"
                : $"GrevID libraries: {string.Join(", ", item.LibraryUsers)}"));
        grid.Children.Add(details);

        var actions = new StackPanel
        {
            Grid.IsSharedSizeScopeProperty = { },
            Margin = new Thickness(20, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(actions, 1);

        if (item.CanUpdate)
        {
            actions.Children.Add(CreateActionButton(
                "Update Machine App",
                definition.AppId,
                Update_Click));
        }

        if (item.CanRepair)
        {
            actions.Children.Add(CreateActionButton(
                "Repair Machine App",
                definition.AppId,
                Repair_Click));
        }

        var isArmed = string.Equals(_armedAppId, definition.AppId, StringComparison.OrdinalIgnoreCase);
        if (item.CanUninstall)
        {
            actions.Children.Add(CreateActionButton(
                isArmed ? "CONFIRM UNINSTALL FROM MACHINE" : "Uninstall from Machine",
                definition.AppId,
                Uninstall_Click));
        }

        if (!item.CanUpdate && !item.CanRepair && !item.CanUninstall)
        {
            actions.Children.Add(new TextBlock
            {
                Text = "No trusted Admin package actions are declared for this app.",
                Foreground = (Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(6)
            });
        }

        grid.Children.Add(actions);
        card.Child = grid;
        return card;
    }

    private TextBlock CreateDetailText(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 5, 0, 0),
        Foreground = (Brush)FindResource("MutedBrush"),
        TextWrapping = TextWrapping.Wrap
    };

    private Button CreateActionButton(string label, string appId, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Tag = appId,
            Content = new TextBlock
            {
                Text = label,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            },
            MinHeight = 54,
            Height = double.NaN,
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !_busy
        };
        button.Click += handler;
        return button;
    }

    private AdminMachineAppItem? FindItem(object sender)
    {
        if (_busy || sender is not Button { Tag: string appId })
        {
            return null;
        }

        return _items.FirstOrDefault(candidate =>
            string.Equals(candidate.Entry.Manifest.Definition.AppId, appId, StringComparison.OrdinalIgnoreCase));
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        var item = FindItem(sender);
        if (item?.CanUpdate == true)
        {
            _armedAppId = null;
            UpdateRequested?.Invoke(item);
        }
    }

    private void Repair_Click(object sender, RoutedEventArgs e)
    {
        var item = FindItem(sender);
        if (item?.CanRepair == true)
        {
            _armedAppId = null;
            RepairRequested?.Invoke(item);
        }
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var item = FindItem(sender);
        if (item?.CanUninstall != true)
        {
            return;
        }

        var appId = item.Entry.Manifest.Definition.AppId;
        if (!string.Equals(_armedAppId, appId, StringComparison.OrdinalIgnoreCase))
        {
            _armedAppId = appId;
            StatusText.Text =
                $"Machine uninstall armed for {item.Entry.Manifest.Definition.Name}. Select CONFIRM UNINSTALL FROM MACHINE to remove it for every GrevID.";
            RenderApps();
            return;
        }

        _armedAppId = null;
        UninstallRequested?.Invoke(item);
    }

    private static string FormatLifecycle(AppLifecycleState state) => state switch
    {
        AppLifecycleState.NotInstalled => "Not installed",
        AppLifecycleState.Installed => "Installed",
        AppLifecycleState.RemovedFromLibrary => "Removed from library",
        AppLifecycleState.Running => "Running",
        AppLifecycleState.UpdateAvailable => "Update available",
        AppLifecycleState.RepairNeeded => "Repair recommended",
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

    private void Back_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);
}
