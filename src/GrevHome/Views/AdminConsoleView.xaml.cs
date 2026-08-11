using System.Windows;
using System.Windows.Controls;
using GrevHome.Apps;

namespace GrevHome.Views;

public sealed record AdminMachineAppItem(
    InstalledAppEntry Entry,
    bool CanUninstall);

public partial class AdminConsoleView : UserControl
{
    private IReadOnlyList<AdminMachineAppItem> _items = Array.Empty<AdminMachineAppItem>();
    private string? _armedAppId;
    private bool _busy;

    public event EventHandler? BackRequested;
    public event Action<InstalledAppEntry>? UninstallRequested;

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

    public void ShowStatus(string message)
    {
        StatusText.Text = message;
    }

    private void RenderApps()
    {
        GlobalAppsPanel.Children.Clear();

        if (_items.Count == 0)
        {
            GlobalAppsPanel.Children.Add(new Border
            {
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 12),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 21, 30)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 51, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Child = new TextBlock
                {
                    Text = "No Global Apps are registered as installed on this Grev Machine.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
                }
            });
            return;
        }

        foreach (var item in _items)
        {
            var definition = item.Entry.Manifest.Definition;
            var card = new Border
            {
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 12),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 21, 30)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 51, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var details = new StackPanel();
            details.Children.Add(new TextBlock
            {
                Text = definition.Name,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            details.Children.Add(new TextBlock
            {
                Text = $"Version {item.Entry.Manifest.Version}  •  {definition.InstallStrategy}  •  {definition.DataStrategy}",
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            details.Children.Add(new TextBlock
            {
                Text = definition.InstallStrategy == InstallStrategy.SystemInstalled
                    ? $"Windows install: {Environment.ExpandEnvironmentVariables(definition.Launch.Executable)}"
                    : $"Grev Home install: {item.Entry.BinaryRoot}",
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            grid.Children.Add(details);

            var isArmed = string.Equals(_armedAppId, definition.AppId, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Tag = definition.AppId,
                Content = !item.CanUninstall
                    ? "No trusted uninstaller yet"
                    : isArmed
                        ? "CONFIRM UNINSTALL FROM MACHINE"
                        : "Uninstall from Machine",
                MinWidth = 260,
                MinHeight = 58,
                Height = double.NaN,
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(20, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !_busy && item.CanUninstall
            };
            button.Click += Uninstall_Click;
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);

            card.Child = grid;
            GlobalAppsPanel.Children.Add(card);
        }
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: string appId })
        {
            return;
        }

        var item = _items.FirstOrDefault(candidate =>
            string.Equals(candidate.Entry.Manifest.Definition.AppId, appId, StringComparison.OrdinalIgnoreCase));
        if (item is null || !item.CanUninstall)
        {
            return;
        }

        if (!string.Equals(_armedAppId, appId, StringComparison.OrdinalIgnoreCase))
        {
            _armedAppId = appId;
            StatusText.Text = $"Machine uninstall armed for {item.Entry.Manifest.Definition.Name}. Select CONFIRM UNINSTALL FROM MACHINE to remove it for every GrevID.";
            RenderApps();
            return;
        }

        _armedAppId = null;
        UninstallRequested?.Invoke(item.Entry);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);
}
