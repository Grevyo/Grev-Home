using System.Windows;
using System.Windows.Controls;
using GrevHome.Apps;
using GrevHome.Sessions;

namespace GrevHome.Views;

public partial class InstalledLibraryView : UserControl
{
    private IReadOnlyList<InstalledAppEntry> _entries = Array.Empty<InstalledAppEntry>();
    private string _filter = "All";
    private SessionUser? _primaryUser;

    public event EventHandler? BackRequested;

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

    private void Render()
    {
        AppsPanel.Children.Clear();

        var visible = _entries.Where(MatchesFilter).ToArray();
        EmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _entries.Count == 0
            ? "Nothing is installed yet. Grev Store and package installation come later."
            : "No installed apps match this filter.";

        foreach (var entry in visible)
        {
            var definition = entry.Manifest.Definition;
            var scope = definition.InstallStrategy switch
            {
                InstallStrategy.SharedBinary => "Shared install",
                InstallStrategy.GrevIdPortable => "GrevID-local install",
                InstallStrategy.SystemInstalled => "Windows-installed",
                _ => "Installed"
            };

            var data = definition.DataStrategy switch
            {
                DataStrategy.GrevId => "Per-account data",
                DataStrategy.Global => "Shared data",
                DataStrategy.NativeAccount => "App-managed account data",
                _ => "Data"
            };

            var button = new Button
            {
                Width = 290,
                Height = 150,
                Margin = new Thickness(8),
                Tag = entry,
                IsEnabled = entry.AvailableToCurrentUser,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = definition.Name,
                            FontSize = 21,
                            FontWeight = FontWeights.SemiBold,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock
                        {
                            Text = $"{definition.Kind} • v{entry.Manifest.Version}",
                            Margin = new Thickness(0, 7, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                            FontSize = 13
                        },
                        new TextBlock
                        {
                            Text = scope,
                            Margin = new Thickness(0, 6, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                            FontSize = 13
                        },
                        new TextBlock
                        {
                            Text = entry.AvailableToCurrentUser ? data : entry.AvailabilityMessage,
                            Margin = new Thickness(0, 4, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
            button.Click += App_Click;
            AppsPanel.Children.Add(button);
        }

        StatusText.Text = _entries.Count == 0
            ? "The Installed Library is ready for real app/package registration. No demonstration apps are created."
            : $"{visible.Length} shown • {_entries.Count} installed for this session context.";
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
        if (sender is not Button { Tag: InstalledAppEntry entry })
        {
            return;
        }

        var definition = entry.Manifest.Definition;
        var dataRoot = entry.DataRoot ?? "Managed by the app/native account";
        StatusText.Text =
            $"{definition.Name} • AppID {definition.AppId} • Binary: {entry.BinaryRoot} • Data: {dataRoot}. Launching comes with the session/process milestone.";
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
