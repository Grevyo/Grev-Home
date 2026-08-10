using System.Windows;
using System.Windows.Controls;
using GrevHome.Runtime;

namespace GrevHome.Views;

public partial class AppKillerView : UserControl
{
    private IReadOnlyList<LaunchSessionSnapshot> _sessions = Array.Empty<LaunchSessionSnapshot>();
    private Guid? _pendingForceClose;

    public event EventHandler? BackRequested;
    public event Action<Guid>? SwitchRequested;
    public event Action<Guid>? CloseRequested;
    public event Action<Guid>? ForceCloseRequested;

    public AppKillerView()
    {
        InitializeComponent();
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

    private void Render()
    {
        SessionsPanel.Children.Clear();
        EmptyText.Visibility = _sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var session in _sessions)
        {
            var participants = string.Join(", ", session.Participants.Select(participant => participant.DisplayName));
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
                Text = $"{session.ProcessIds.Count} tracked process{(session.ProcessIds.Count == 1 ? string.Empty : "es")} • {session.State}",
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                FontSize = 12
            });
            content.Children.Add(new TextBlock
            {
                Text = $"Participants: {participants}",
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            actions.Children.Add(CreateButton("Switch", session.LaunchSessionId, Switch_Click));
            actions.Children.Add(CreateButton(
                session.State == LaunchSessionState.Closing ? "Closing…" : "Close",
                session.LaunchSessionId,
                Close_Click,
                session.State != LaunchSessionState.Closing));
            actions.Children.Add(CreateButton(
                _pendingForceClose == session.LaunchSessionId ? "CONFIRM FORCE CLOSE" : "Force Close",
                session.LaunchSessionId,
                Force_Click));
            content.Children.Add(actions);

            SessionsPanel.Children.Add(new Border
            {
                Width = 390,
                MinHeight = 205,
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

        if (_sessions.Count == 0)
        {
            StatusText.Text = "Nothing is running through Grev Home right now.";
        }
        else if (!_pendingForceClose.HasValue)
        {
            StatusText.Text = "Use Close first. Force Close can interrupt saves/config writes and requires a second press on the same app.";
        }
    }

    private static Button CreateButton(
        string text,
        Guid launchSessionId,
        RoutedEventHandler handler,
        bool enabled = true)
    {
        var button = new Button
        {
            Content = text,
            Width = text.StartsWith("CONFIRM", StringComparison.Ordinal) ? 205 : 120,
            Height = 46,
            Margin = new Thickness(0, 0, 8, 8),
            IsEnabled = enabled,
            Tag = launchSessionId
        };
        button.Click += handler;
        return button;
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        _pendingForceClose = null;
        if (sender is Button { Tag: Guid launchSessionId })
        {
            SwitchRequested?.Invoke(launchSessionId);
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
            button.Content = "CONFIRM FORCE CLOSE";
            button.Width = 205;
            StatusText.Text = "Force Close may interrupt saves/config writes. Press CONFIRM FORCE CLOSE again to terminate this app's tracked process tree.";
            return;
        }

        _pendingForceClose = null;
        ForceCloseRequested?.Invoke(launchSessionId);
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);
}
