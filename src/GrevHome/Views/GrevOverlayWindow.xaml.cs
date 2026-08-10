using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GrevHome.Input;
using GrevHome.Runtime;

namespace GrevHome.Views;

public partial class GrevOverlayWindow : Window
{
    private IReadOnlyList<LaunchSessionSnapshot> _sessions = Array.Empty<LaunchSessionSnapshot>();
    private Guid? _foregroundSessionId;
    private OverlayMode _mode = OverlayMode.Home;

    public event Action<Guid>? ResumeRequested;
    public event EventHandler? ReturnHomeRequested;
    public event EventHandler? RunningAppsRequested;
    public event EventHandler? AppKillerRequested;
    public event Action<Guid>? SwitchRequested;
    public event Action<Guid>? RestartRequested;
    public event Action<Guid>? CloseRequested;

    public GrevOverlayWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            if (IsVisible)
            {
                Activate();
            }
        };
    }

    public bool IsOpen => IsVisible;

    public void Open(
        IReadOnlyList<LaunchSessionSnapshot> sessions,
        LaunchSessionSnapshot? foregroundSession)
    {
        _sessions = sessions;
        _foregroundSessionId = foregroundSession?.LaunchSessionId;
        _mode = OverlayMode.Home;
        Render();

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        FocusFirstButton();
    }

    public void Refresh(IReadOnlyList<LaunchSessionSnapshot> sessions)
    {
        _sessions = sessions;
        if (_foregroundSessionId.HasValue && _sessions.All(session => session.LaunchSessionId != _foregroundSessionId.Value))
        {
            _foregroundSessionId = null;
        }

        if (IsVisible)
        {
            Render();
            FocusFirstButton();
        }
    }

    public void Dismiss()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    public void HandleControllerInput(InputAction action)
    {
        switch (action)
        {
            case InputAction.Up:
                MoveFocus(FocusNavigationDirection.Up);
                break;
            case InputAction.Down:
                MoveFocus(FocusNavigationDirection.Down);
                break;
            case InputAction.Left:
                MoveFocus(FocusNavigationDirection.Left);
                break;
            case InputAction.Right:
                MoveFocus(FocusNavigationDirection.Right);
                break;
            case InputAction.Accept:
                ActivateFocusedButton();
                break;
            case InputAction.Back:
                HandleBack();
                break;
        }
    }

    private void Render()
    {
        ActionPanel.Children.Clear();

        if (_mode == OverlayMode.Switcher)
        {
            RenderSwitcher();
            return;
        }

        var foreground = _foregroundSessionId.HasValue
            ? _sessions.FirstOrDefault(session => session.LaunchSessionId == _foregroundSessionId.Value)
            : null;

        TitleText.Text = "GREV OVERLAY";
        SubtitleText.Text = foreground is null
            ? $"{_sessions.Count} Grev Home app session{(_sessions.Count == 1 ? string.Empty : "s")} currently running."
            : $"Current app: {foreground.AppName} • {FormatElapsed(foreground.Elapsed)}";

        AddAction(
            foreground is null ? "Resume" : $"Resume {foreground.AppName}",
            foreground is not null,
            () =>
            {
                if (foreground is not null)
                {
                    Dismiss();
                    ResumeRequested?.Invoke(foreground.LaunchSessionId);
                }
            });

        AddAction(
            "Switch App",
            _sessions.Count > 0,
            () =>
            {
                _mode = OverlayMode.Switcher;
                Render();
                FocusFirstButton();
            });

        AddAction(
            foreground is null ? "Restart Current App" : $"Restart {foreground.AppName}",
            foreground?.State == LaunchSessionState.Running,
            () =>
            {
                if (foreground is not null)
                {
                    Dismiss();
                    RestartRequested?.Invoke(foreground.LaunchSessionId);
                }
            });

        AddAction(
            foreground is null ? "Close Current App" : $"Close {foreground.AppName}",
            foreground is not null,
            () =>
            {
                if (foreground is not null)
                {
                    Dismiss();
                    CloseRequested?.Invoke(foreground.LaunchSessionId);
                }
            });

        AddAction("Running Apps", true, () =>
        {
            Dismiss();
            RunningAppsRequested?.Invoke(this, EventArgs.Empty);
        });

        AddAction("App Killer", true, () =>
        {
            Dismiss();
            AppKillerRequested?.Invoke(this, EventArgs.Empty);
        });

        AddAction("Return to Grev Home", true, () =>
        {
            Dismiss();
            ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
        });

        HintText.Text = "A Select   •   B Resume / Close Overlay";
    }

    private void RenderSwitcher()
    {
        TitleText.Text = "SWITCH APP";
        SubtitleText.Text = _sessions.Count == 0
            ? "Nothing launched through Grev Home is currently running."
            : "Choose a running Grev Home app to bring it to the foreground.";

        foreach (var session in _sessions)
        {
            var participants = string.Join(", ", session.Participants.Select(participant => participant.DisplayName));
            AddAction(
                $"{session.AppName}\n{FormatElapsed(session.Elapsed)} • {participants}",
                session.State is LaunchSessionState.Running or LaunchSessionState.Closing,
                () =>
                {
                    Dismiss();
                    SwitchRequested?.Invoke(session.LaunchSessionId);
                },
                height: 82);
        }

        AddAction("Back", true, () =>
        {
            _mode = OverlayMode.Home;
            Render();
            FocusFirstButton();
        });

        HintText.Text = "A Switch   •   B Back";
    }

    private void AddAction(string label, bool isEnabled, Action action, double height = 64)
    {
        var button = new Button
        {
            Content = label,
            Height = height,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            IsEnabled = isEnabled,
            Tag = action
        };
        button.Click += ActionButton_Click;
        ActionPanel.Children.Add(button);
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Action action } && ((Button)sender).IsEnabled)
        {
            action();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var action = e.Key switch
        {
            Key.Up => InputAction.Up,
            Key.Down => InputAction.Down,
            Key.Left => InputAction.Left,
            Key.Right => InputAction.Right,
            Key.Enter or Key.Space => InputAction.Accept,
            Key.Escape => InputAction.Back,
            _ => (InputAction?)null
        };

        if (action is null)
        {
            return;
        }

        HandleControllerInput(action.Value);
        e.Handled = true;
    }

    private void HandleBack()
    {
        if (_mode == OverlayMode.Switcher)
        {
            _mode = OverlayMode.Home;
            Render();
            FocusFirstButton();
            return;
        }

        var foreground = _foregroundSessionId.HasValue
            ? _sessions.FirstOrDefault(session => session.LaunchSessionId == _foregroundSessionId.Value)
            : null;

        Dismiss();
        if (foreground is not null)
        {
            ResumeRequested?.Invoke(foreground.LaunchSessionId);
        }
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";

    private static void MoveFocus(FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is UIElement focused)
        {
            focused.MoveFocus(new TraversalRequest(direction));
        }
    }

    private static void ActivateFocusedButton()
    {
        if (Keyboard.FocusedElement is Button { IsEnabled: true } button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }
    }

    private void FocusFirstButton()
    {
        var first = FindVisualChildren<Button>(ActionPanel)
            .FirstOrDefault(button => button.IsVisible && button.IsEnabled);
        first?.Focus();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private enum OverlayMode
    {
        Home,
        Switcher
    }
}
