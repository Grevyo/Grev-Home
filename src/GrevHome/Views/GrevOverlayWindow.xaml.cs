using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GrevHome.Input;
using GrevHome.Runtime;

namespace GrevHome.Views;

public sealed record ControllerGuideItem(string Control, string Action);

public partial class GrevOverlayWindow : Window
{
    private IReadOnlyList<LaunchSessionSnapshot> _sessions = Array.Empty<LaunchSessionSnapshot>();
    private Guid? _foregroundSessionId;
    private ControllerGuideContent? _controllerGuide;
    private OverlayMode _mode = OverlayMode.Home;

    public event Action<Guid>? ResumeRequested;
    public event EventHandler? ReturnHomeRequested;
    public event EventHandler? RunningAppsRequested;
    public event EventHandler? AppKillerRequested;
    public event Action<Guid>? SwitchRequested;
    public event Action<Guid>? RestartRequested;
    public event Action<Guid>? CloseRequested;
    public event Action<string, string>? ControllerGuideDontShowAgainRequested;

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
        _controllerGuide = null;
        _mode = OverlayMode.Home;
        Render();
        ShowAndFocus();
    }

    public void OpenControllerGuide(
        string appId,
        string? grevId,
        string title,
        string summary,
        string returnHomeShortcut,
        string overlayShortcut,
        IReadOnlyList<ControllerGuideItem> controls)
    {
        _controllerGuide = new ControllerGuideContent(
            appId,
            grevId,
            title,
            summary,
            returnHomeShortcut,
            overlayShortcut,
            controls);
        _mode = OverlayMode.ControllerGuide;
        Render();
        ShowAndFocus();
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

    private void ShowAndFocus()
    {
        if (!IsVisible)
        {
            Show();
        }

        Activate();
        FocusFirstButton();
    }

    private void Render()
    {
        ActionPanel.Children.Clear();

        if (_mode == OverlayMode.ControllerGuide)
        {
            RenderControllerGuide();
            return;
        }

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

    private void RenderControllerGuide()
    {
        var guide = _controllerGuide;
        if (guide is null)
        {
            _mode = OverlayMode.Home;
            Render();
            return;
        }

        TitleText.Text = guide.Title;
        SubtitleText.Text = guide.Summary;

        AddSectionHeading("SYSTEM SHORTCUTS");
        var shortcutGrid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(-5, 0, -5, 16)
        };
        shortcutGrid.Children.Add(CreateGuideCard("Return Home", guide.ReturnHomeShortcut, emphasize: true));
        shortcutGrid.Children.Add(CreateGuideCard("Grev Overlay", guide.OverlayShortcut, emphasize: true));
        ActionPanel.Children.Add(shortcutGrid);

        AddSectionHeading("CONTROLLER");
        var controlGrid = new UniformGrid
        {
            Columns = 3,
            Margin = new Thickness(-5, 0, -5, 18)
        };

        foreach (var item in guide.Controls.Take(12))
        {
            controlGrid.Children.Add(CreateGuideCard(item.Control, item.Action, emphasize: false));
        }
        ActionPanel.Children.Add(controlGrid);

        AddAction("Close", true, Dismiss, minimumHeight: 54);
        AddAction(
            string.IsNullOrWhiteSpace(guide.GrevId)
                ? "Don't Show Again (requires a persistent profile)"
                : "Don't Show Again",
            !string.IsNullOrWhiteSpace(guide.GrevId),
            () =>
            {
                if (!string.IsNullOrWhiteSpace(guide.GrevId))
                {
                    ControllerGuideDontShowAgainRequested?.Invoke(guide.GrevId, guide.AppId);
                }
                Dismiss();
            },
            minimumHeight: 54);

        HintText.Text = "A Select   •   B Close Guide   •   Right Stick / Triggers Pointer";
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
                minimumHeight: 82);
        }

        AddAction("Back", true, () =>
        {
            _mode = OverlayMode.Home;
            Render();
            FocusFirstButton();
        });

        HintText.Text = "A Switch   •   B Back";
    }

    private void AddSectionHeading(string text)
    {
        ActionPanel.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
    }

    private Border CreateGuideCard(string title, string value, bool emphasize)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = value,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 13,
            Foreground = emphasize
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        return new Border
        {
            MinHeight = 82,
            Margin = new Thickness(5),
            Padding = new Thickness(13, 11, 13, 11),
            Background = new SolidColorBrush(Color.FromRgb(19, 24, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(49, 59, 78)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = content
        };
    }

    private void AddAction(string label, bool isEnabled, Action action, double minimumHeight = 64)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = label,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            },
            MinHeight = minimumHeight,
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = isEnabled,
            Tag = action
        };
        button.Click += ActionButton_Click;
        ActionPanel.Children.Add(button);
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Action action, IsEnabled: true })
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
        if (_mode == OverlayMode.ControllerGuide)
        {
            Dismiss();
            return;
        }

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

    private sealed record ControllerGuideContent(
        string AppId,
        string? GrevId,
        string Title,
        string Summary,
        string ReturnHomeShortcut,
        string OverlayShortcut,
        IReadOnlyList<ControllerGuideItem> Controls);

    private enum OverlayMode
    {
        Home,
        Switcher,
        ControllerGuide
    }
}
