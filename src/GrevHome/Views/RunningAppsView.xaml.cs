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
    public event Action<Guid>? SwitchRequested;
    public event Action<Guid>? RestartRequested;
    public event Action<Guid>? CloseRequested;

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

    public void ShowStatus(string message)
    {
        StatusText.Text = message;
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

            var switchButton = new Button
            {
                Content = "Switch",
                Width = 100,
                Height = 46,
                Margin = new Thickness(0, 12, 8, 0),
                Tag = session.LaunchSessionId
            };
            switchButton.Click += Switch_Click;

            var restartButton = new Button
            {
                Content = "Restart",
                Width = 100,
                Height = 46,
                Margin = new Thickness(0, 12, 8, 0),
                IsEnabled = session.State == LaunchSessionState.Running,
                Tag = session.LaunchSessionId
            };
            restartButton.Click += Restart_Click;

            var closeButton = new Button
            {
                Content = session.State == LaunchSessionState.Closing ? "Closing…" : "Close",
                Width = 100,
                Height = 46,
                Margin = new Thickness(0, 12, 0, 0),
                IsEnabled = session.State != LaunchSessionState.Closing,
                Tag = session.LaunchSessionId
            };
            closeButton.Click += Close_Click;

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(switchButton);
            actions.Children.Add(restartButton);
            actions.Children.Add(closeButton);

            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = session.AppName,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            content.Children.Add(new TextBlock
            {
                Text = $"AppID {session.AppId}",
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                FontSize = 12
            });
            content.Children.Add(elapsed);
            content.Children.Add(new TextBlock
            {
                Text = $"Playing: {participants}",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = $"{processes} • {session.State}",
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                FontSize = 12
            });
            content.Children.Add(actions);

            SessionsPanel.Children.Add(new Border
            {
                Width = 360,
                MinHeight = 220,
                Margin = new Thickness(8),
                Padding = new Thickness(18),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(17, 21, 30)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(43, 51, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = content
            });
        }

        StatusText.Text = _sessions.Count == 0
            ? "Nothing is running through Grev Home right now."
            : "Switch foregrounds an app. Restart safely closes and relaunches the same managed AppID with its original launch participants. Close requests a normal shutdown; force-close lives in App Killer.";

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

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid launchSessionId })
        {
            SwitchRequested?.Invoke(launchSessionId);
        }
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid launchSessionId })
        {
            RestartRequested?.Invoke(launchSessionId);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid launchSessionId })
        {
            CloseRequested?.Invoke(launchSessionId);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);
}
