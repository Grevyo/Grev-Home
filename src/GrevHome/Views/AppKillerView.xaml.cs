using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GrevHome.Runtime;

namespace GrevHome.Views;

public partial class AppKillerView : UserControl
{
    private readonly DispatcherTimer _elapsedTimer;
    private readonly Dictionary<Guid, TextBlock> _elapsedLabels = new();
    private IReadOnlyList<LaunchSessionSnapshot> _sessions = Array.Empty<LaunchSessionSnapshot>();
    private Guid? _pendingForceClose;
    private string? _preferredAppId;
    private Button? _preferredFocusButton;

    public event EventHandler? BackRequested;
    public event Action<Guid>? SwitchRequested;
    public event Action<Guid>? RestartRequested;
    public event Action<Guid>? CloseRequested;
    public event Action<Guid>? ForceCloseRequested;

    public AppKillerView()
    {
        InitializeComponent();
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += (_, _) => UpdateElapsedLabels();
        Loaded += (_, _) =>
        {
            _elapsedTimer.Start();
            FocusPreferredSession();
        };
        Unloaded += (_, _) => _elapsedTimer.Stop();
    }

    public void SetPreferredApp(string? appId)
    {
        _preferredAppId = string.IsNullOrWhiteSpace(appId) ? null : appId;
    }

    public void SetSessions(IReadOnlyList<LaunchSessionSnapshot> sessions)
    {
        _sessions = sessions;
        if (_pendingForceClose.HasValue && _sessions.All(session => session.LaunchSessionId != _pendingForceClose.Value))
        {
            _pendingForceClose = null;
        }

        Render();
    }

    public void ShowStatus(string message) => StatusText.Text = message;

    public void FocusPreferredSession()
    {
        if (_preferredAppId is null || _preferredFocusButton is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_preferredFocusButton is { IsVisible: true, IsEnabled: true })
            {
                _preferredFocusButton.Focus();
            }
        }));
    }

    private void Render()
    {
        SessionsPanel.Children.Clear();
        _elapsedLabels.Clear();
        _preferredFocusButton = null;

        EmptyText.Visibility = _sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionSummaryText.Text = _sessions.Count.ToString();
        ProcessSummaryText.Text = _sessions.Sum(session => session.ProcessIds.Count).ToString();

        var ordered = _sessions
            .OrderByDescending(session => IsPreferred(session))
            .ThenByDescending(session => session.StartedAtUtc)
            .ToArray();

        foreach (var session in ordered)
        {
            SessionsPanel.Children.Add(CreateSessionCard(session));
        }

        if (_sessions.Count == 0)
        {
            StatusText.Text = "Nothing is running through Grev Home right now. App Killer only acts on sessions Grev Home is actively tracking.";
        }
        else if (_pendingForceClose.HasValue)
        {
            StatusText.Text = "Force Kill is armed for one session. Press CONFIRM FORCE KILL APP on that same session to terminate its tracked process tree, or choose another action to cancel.";
        }
        else if (_preferredAppId is not null && ordered.Any(IsPreferred))
        {
            StatusText.Text = "The app you opened App Killer from is shown first. Use Close Normally where possible; Force Kill is for an app that will not close safely.";
        }
        else
        {
            StatusText.Text = "Use Switch to return to an app, Restart to relaunch it, or Close Normally first. Force Kill can interrupt saves/configuration writes and requires a second press.";
        }

        UpdateElapsedLabels();
    }

    private Border CreateSessionCard(LaunchSessionSnapshot session)
    {
        var preferred = IsPreferred(session);
        var participants = session.Participants.Count == 0
            ? "None recorded"
            : string.Join(", ", session.Participants.Select(participant => participant.DisplayName));
        var owner = string.IsNullOrWhiteSpace(session.PrimaryGrevId)
            ? "Shared / no persistent GrevID"
            : session.PrimaryGrevId;
        var processIds = session.ProcessIds.Take(12).ToArray();
        var processList = processIds.Length == 0
            ? "None"
            : string.Join(", ", processIds) +
              (session.ProcessIds.Count > processIds.Length
                  ? $"  +{session.ProcessIds.Count - processIds.Length} more"
                  : string.Empty);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = session.AppName,
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        var elapsed = new TextBlock
        {
            Margin = new Thickness(0, 7, 0, 0),
            Foreground = (Brush)FindResource("AccentBrush"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        _elapsedLabels[session.LaunchSessionId] = elapsed;
        content.Children.Add(elapsed);

        content.Children.Add(CreateDetailText(
            $"State: {session.State}  •  Root PID {session.RootProcessId}  •  {session.ProcessIds.Count} tracked process{(session.ProcessIds.Count == 1 ? string.Empty : "es")}"));
        content.Children.Add(CreateDetailText($"Primary owner: {owner}"));
        content.Children.Add(CreateDetailText($"Participants: {participants}"));
        content.Children.Add(CreateDetailText($"Process IDs: {processList}"));
        content.Children.Add(CreateDetailText($"Session: {session.LaunchSessionId.ToString()[..8]}  •  Started {session.StartedAtUtc.ToLocalTime():dd MMM yyyy HH:mm:ss}"));

        if (!string.IsNullOrWhiteSpace(session.FailureMessage))
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Runtime message: {session.FailureMessage}",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(224, 112, 122)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var switchButton = CreateButton("Switch to App", session.LaunchSessionId, Switch_Click);
        actions.Children.Add(switchButton);
        actions.Children.Add(CreateButton(
            "Restart App",
            session.LaunchSessionId,
            Restart_Click,
            session.State == LaunchSessionState.Running));
        actions.Children.Add(CreateButton(
            session.State == LaunchSessionState.Closing ? "Closing…" : "Close Normally",
            session.LaunchSessionId,
            Close_Click,
            session.State != LaunchSessionState.Closing));
        actions.Children.Add(CreateButton(
            _pendingForceClose == session.LaunchSessionId ? "CONFIRM FORCE KILL APP" : "Force Kill App",
            session.LaunchSessionId,
            Force_Click));
        content.Children.Add(actions);

        if (preferred)
        {
            _preferredFocusButton = switchButton;
        }

        return new Border
        {
            MaxWidth = 1120,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(22),
            Background = new SolidColorBrush(Color.FromRgb(17, 21, 30)),
            BorderBrush = preferred
                ? (Brush)FindResource("AccentBrush")
                : new SolidColorBrush(Color.FromRgb(43, 51, 68)),
            BorderThickness = new Thickness(preferred ? 2 : 1),
            CornerRadius = new CornerRadius(14),
            Child = content
        };
    }

    private TextBlock CreateDetailText(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 6, 0, 0),
        Foreground = (Brush)FindResource("MutedBrush"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap
    };

    private Button CreateButton(
        string text,
        Guid launchSessionId,
        RoutedEventHandler handler,
        bool enabled = true)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource("AppKillerActionButtonStyle"),
            IsEnabled = enabled,
            Tag = launchSessionId
        };
        button.Click += handler;
        return button;
    }

    private bool IsPreferred(LaunchSessionSnapshot session) =>
        _preferredAppId is not null &&
        string.Equals(session.AppId, _preferredAppId, StringComparison.OrdinalIgnoreCase);

    private void UpdateElapsedLabels()
    {
        foreach (var session in _sessions)
        {
            if (_elapsedLabels.TryGetValue(session.LaunchSessionId, out var label))
            {
                var elapsed = session.Elapsed;
                label.Text = elapsed.TotalHours >= 1
                    ? $"Running {((int)elapsed.TotalHours)}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                    : $"Running {elapsed.Minutes}:{elapsed.Seconds:00}";
            }
        }
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        _pendingForceClose = null;
        if (sender is Button { Tag: Guid launchSessionId })
        {
            SwitchRequested?.Invoke(launchSessionId);
        }
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        _pendingForceClose = null;
        if (sender is Button { Tag: Guid launchSessionId })
        {
            RestartRequested?.Invoke(launchSessionId);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _pendingForceClose = null;
        if (sender is Button { Tag: Guid launchSessionId })
        {
            CloseRequested?.Invoke(launchSessionId);
        }
    }

    private void Force_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid launchSessionId } button)
        {
            return;
        }

        if (_pendingForceClose != launchSessionId)
        {
            _pendingForceClose = launchSessionId;
            button.Content = "CONFIRM FORCE KILL APP";
            StatusText.Text = "Force Kill may interrupt saves or configuration writes. Press CONFIRM FORCE KILL APP again on this same session to terminate its tracked process tree.";
            return;
        }

        _pendingForceClose = null;
        ForceCloseRequested?.Invoke(launchSessionId);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);
}
