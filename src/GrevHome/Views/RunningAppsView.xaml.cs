using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GrevHome.Runtime;

namespace GrevHome.Views;

public partial class RunningAppsView : UserControl
{
    private readonly DispatcherTimer _elapsedTimer;
    private readonly Dictionary<Guid, TextBlock> _elapsedLabels = new();
    private IReadOnlyList<LaunchSessionSnapshot> _sessions = Array.Empty<LaunchSessionSnapshot>();

    public event EventHandler? BackRequested;

    public RunningAppsView()
    {
        InitializeComponent();
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += (_, _) => UpdateElapsedLabels();
        Loaded += (_, _) => _elapsedTimer.Start();
        Unloaded += (_, _) => _elapsedTimer.Stop();
    }

    public void SetSessions(IReadOnlyList<LaunchSessionSnapshot> sessions)
    {
        _sessions = sessions;
        Render();
    }

    private void Render()
    {
        SessionsPanel.Children.Clear();
        _elapsedLabels.Clear();
        EmptyText.Visibility = _sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var session in _sessions)
        {
            var participants = string.Join(", ", session.Participants.Select(participant => participant.DisplayName));
            var processes = session.ProcessIds.Count == 1
                ? $"PID {session.RootProcessId}"
                : $"{session.ProcessIds.Count} tracked processes";

            var elapsed = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
                FontSize = 15
            };
            _elapsedLabels[session.LaunchSessionId] = elapsed;

            var card = new Border
            {
                Width = 340,
                MinHeight = 170,
                Margin = new Thickness(8),
                Padding = new Thickness(18),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(17, 21, 30)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(43, 51, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = session.AppName,
                            FontSize = 22,
                            FontWeight = FontWeights.SemiBold,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock
                        {
                            Text = $"AppID {session.AppId}",
                            Margin = new Thickness(0, 6, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                            FontSize = 12
                        },
                        elapsed,
                        new TextBlock
                        {
                            Text = $"Playing: {participants}",
                            Margin = new Thickness(0, 8, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                            FontSize = 13,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = processes,
                            Margin = new Thickness(0, 6, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                            FontSize = 12
                        }
                    }
                }
            };

            SessionsPanel.Children.Add(card);
        }

        StatusText.Text = _sessions.Count == 0
            ? "Nothing is running through Grev Home right now."
            : $"{_sessions.Count} active Grev Home session{(_sessions.Count == 1 ? string.Empty : "s")}. Switching and closing arrive next.";

        UpdateElapsedLabels();
    }

    private void UpdateElapsedLabels()
    {
        foreach (var session in _sessions)
        {
            if (_elapsedLabels.TryGetValue(session.LaunchSessionId, out var label))
            {
                var elapsed = session.Elapsed;
                label.Text = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00} running"
                    : $"{elapsed.Minutes}:{elapsed.Seconds:00} running";
            }
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);
}
