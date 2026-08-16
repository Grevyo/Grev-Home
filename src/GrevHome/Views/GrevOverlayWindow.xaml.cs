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
    private Guid? _pendingAppKillerForceClose;
    private readonly Dictionary<Guid, Button> _appKillerForceButtons = new();
    private OverlayMode _mode = OverlayMode.Home;

    public event Action<Guid>? ResumeRequested;
    public event EventHandler? ReturnHomeRequested;
    public event EventHandler? RunningAppsRequested;
    public event Action<Guid>? SwitchRequested;
    public event Action<Guid>? RestartRequested;
    public event Action<Guid>? CloseRequested;
    public event Action<Guid>? AppKillerCloseRequested;
    public event Action<Guid>? AppKillerForceCloseRequested;
    public event Action<string, string>? ControllerGuideDontShowAgainRequested;
    public event Action<string, string>? ControllerGuideDisableControllerProfileRequested;

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
        _pendingAppKillerForceClose = null;
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
        IReadOnlyList<ControllerGuideItem> controls,
        string? quickDisableControllerProfileLabel = null,
        string? quickDisableControllerProfileDescription = null)
    {
        _controllerGuide = new ControllerGuideContent(
            appId,
            grevId,
            title,
            summary,
            returnHomeShortcut,
            overlayShortcut,
            controls,
            quickDisableControllerProfileLabel,
            quickDisableControllerProfileDescription);
        _pendingAppKillerForceClose = null;
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

        if (_pendingAppKillerForceClose.HasValue &&
            _sessions.All(session => session.LaunchSessionId != _pendingAppKillerForceClose.Value))
        {
            _pendingAppKillerForceClose = null;
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
        GuideActionPanel.Children.Clear();
        GuideActionPanel.Visibility = Visibility.Collapsed;
        _appKillerForceButtons.Clear();

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

        if (_mode == OverlayMode.AppKiller)
        {
            RenderAppKiller();
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

        // App Killer is part of the external-app overlay now. Do not make MainWindow visible just
        // to manage a running session; doing so intentionally disables external-app input mode.
        AddAction("App Killer", true, () =>
        {
            _pendingAppKillerForceClose = null;
            _mode = OverlayMode.AppKiller;
            Render();
            FocusFirstButton();
        });

        AddAction("Return to Grev Home", true, () =>
        {
            Dismiss();
            ReturnHomeRequested?.Invoke(this, EventArgs.Empty);
        });

        ContentScrollViewer.ScrollToTop();
        HintText.Text = "A Select   •   B Resume / Close Overlay";
    }

    private void RenderAppKiller()
    {
        TitleText.Text = "APP KILLER";
        SubtitleText.Text = _sessions.Count == 0
            ? "Nothing launched through Grev Home is currently running."
            : "Manage tracked apps without leaving the active app/controller session. Close Normally is the safe first choice; Force Kill needs a second press.";

        var ordered = _sessions
            .OrderByDescending(session => session.LaunchSessionId == _foregroundSessionId)
            .ThenByDescending(session => session.StartedAtUtc)
            .ToArray();

        if (ordered.Length == 0)
        {
            ActionPanel.Children.Add(new TextBlock
            {
                Text = "No active Grev Home sessions.",
                Margin = new Thickness(0, 8, 0, 18),
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap
            });
        }

        foreach (var session in ordered)
        {
            ActionPanel.Children.Add(CreateAppKillerSessionCard(session));
        }

        AddAction("Back to Overlay", true, () =>
        {
            _pendingAppKillerForceClose = null;
            _mode = OverlayMode.Home;
            Render();
            FocusFirstButton();
        }, minimumHeight: 56);

        ContentScrollViewer.ScrollToTop();
        HintText.Text = _pendingAppKillerForceClose.HasValue
            ? "A Confirm Force Kill   •   B Back   •   Force Kill can interrupt saves/config writes"
            : "A Select   •   B Back to Overlay";
    }

    private Border CreateAppKillerSessionCard(LaunchSessionSnapshot session)
    {
        var foreground = session.LaunchSessionId == _foregroundSessionId;
        var participants = session.Participants.Count == 0
            ? "No participants recorded"
            : string.Join(", ", session.Participants.Select(participant => participant.DisplayName));

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = foreground ? $"{session.AppName}  •  CURRENT" : session.AppName,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground ? (Brush)FindResource("AccentBrush") : Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = $"{session.State}  •  {FormatElapsed(session.Elapsed)}  •  {session.ProcessIds.Count} tracked process{(session.ProcessIds.Count == 1 ? string.Empty : "es")}  •  {participants}",
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 12,
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        var actions = new WrapPanel { Margin = new Thickness(-4, 14, -4, -4) };
        actions.Children.Add(CreateAppKillerActionButton(
            "Switch to App",
            () =>
            {
                _pendingAppKillerForceClose = null;
                Dismiss();
                SwitchRequested?.Invoke(session.LaunchSessionId);
            },
            enabled: session.State is LaunchSessionState.Running or LaunchSessionState.Closing));
        actions.Children.Add(CreateAppKillerActionButton(
            "Restart App",
            () =>
            {
                _pendingAppKillerForceClose = null;
                Dismiss();
                RestartRequested?.Invoke(session.LaunchSessionId);
            },
            enabled: session.State == LaunchSessionState.Running));
        actions.Children.Add(CreateAppKillerActionButton(
            session.State == LaunchSessionState.Closing ? "Closing…" : "Close Normally",
            () =>
            {
                _pendingAppKillerForceClose = null;
                AppKillerCloseRequested?.Invoke(session.LaunchSessionId);
            },
            enabled: session.State != LaunchSessionState.Closing));

        var forceButton = CreateAppKillerActionButton(
            _pendingAppKillerForceClose == session.LaunchSessionId
                ? "CONFIRM FORCE KILL APP"
                : "Force Kill App",
            () => ArmOrForceKillFromOverlay(session.LaunchSessionId));
        _appKillerForceButtons[session.LaunchSessionId] = forceButton;
        actions.Children.Add(forceButton);
        content.Children.Add(actions);

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(18),
            Background = new SolidColorBrush(Color.FromRgb(17, 21, 30)),
            BorderBrush = foreground
                ? (Brush)FindResource("AccentBrush")
                : new SolidColorBrush(Color.FromRgb(49, 59, 78)),
            BorderThickness = new Thickness(foreground ? 2 : 1),
            CornerRadius = new CornerRadius(12),
            Child = content
        };
    }

    private Button CreateAppKillerActionButton(string label, Action action, bool enabled = true)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = label,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            MinWidth = 190,
            MaxWidth = 230,
            MinHeight = 54,
            Height = double.NaN,
            Margin = new Thickness(4),
            Padding = new Thickness(12, 9, 12, 9),
            IsEnabled = enabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = action
        };
        button.Click += ActionButton_Click;
        return button;
    }

    private void ArmOrForceKillFromOverlay(Guid launchSessionId)
    {
        if (_pendingAppKillerForceClose != launchSessionId)
        {
            _pendingAppKillerForceClose = launchSessionId;
            Render();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_appKillerForceButtons.TryGetValue(launchSessionId, out var confirm) &&
                    confirm.IsVisible && confirm.IsEnabled)
                {
                    confirm.Focus();
                }
            }));
            return;
        }

        _pendingAppKillerForceClose = null;
        AppKillerForceCloseRequested?.Invoke(launchSessionId);
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
        GuideActionPanel.Visibility = Visibility.Visible;

        AddSectionHeading("SYSTEM SHORTCUTS");
        var shortcutGrid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(-4, 0, -4, 10)
        };
        shortcutGrid.Children.Add(CreateShortcutCard("Return Home", guide.ReturnHomeShortcut));
        shortcutGrid.Children.Add(CreateShortcutCard("Grev Overlay", guide.OverlayShortcut));
        ActionPanel.Children.Add(shortcutGrid);

        AddSectionHeading("CONTROLLER");
        var controlGrid = new UniformGrid
        {
            Columns = 4,
            Margin = new Thickness(-4, 0, -4, 10)
        };

        foreach (var item in guide.Controls.Take(12))
        {
            controlGrid.Children.Add(CreateControllerGuideCard(item));
        }
        ActionPanel.Children.Add(controlGrid);

        if (!string.IsNullOrWhiteSpace(guide.QuickDisableControllerProfileDescription))
        {
            AddSectionHeading("SETUP HELPER");
            ActionPanel.Children.Add(CreateSetupHelperCard(guide.QuickDisableControllerProfileDescription));
        }

        // Guide actions live outside the ScrollViewer. Controller focus therefore cannot drag
        // the help content to the bottom merely because the first actionable control is focused.
        AddGuideFooterAction("Close", true, Dismiss);
        AddGuideFooterAction(
            string.IsNullOrWhiteSpace(guide.GrevId)
                ? "Don't Show Again\nPersistent profile required"
                : "Don't Show Again",
            !string.IsNullOrWhiteSpace(guide.GrevId),
            () =>
            {
                if (!string.IsNullOrWhiteSpace(guide.GrevId))
                {
                    ControllerGuideDontShowAgainRequested?.Invoke(guide.GrevId, guide.AppId);
                }
                Dismiss();
            });

        if (!string.IsNullOrWhiteSpace(guide.QuickDisableControllerProfileLabel))
        {
            AddGuideFooterAction(
                string.IsNullOrWhiteSpace(guide.GrevId)
                    ? $"{guide.QuickDisableControllerProfileLabel}\nPersistent profile required"
                    : guide.QuickDisableControllerProfileLabel,
                !string.IsNullOrWhiteSpace(guide.GrevId),
                () =>
                {
                    if (!string.IsNullOrWhiteSpace(guide.GrevId))
                    {
                        ControllerGuideDisableControllerProfileRequested?.Invoke(guide.GrevId, guide.AppId);
                    }
                    Dismiss();
                });
        }

        ContentScrollViewer.ScrollToTop();
        HintText.Text = "D-Pad / A Select   •   B Close Guide   •   Left Stick Scroll   •   Right Stick / Triggers Pointer";
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

        ContentScrollViewer.ScrollToTop();
        HintText.Text = "A Switch   •   B Back";
    }

    private void AddSectionHeading(string text)
    {
        ActionPanel.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 0, 0, 6),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
    }

    private Border CreateShortcutCard(string title, string value)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = value,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 231, 240)),
            TextWrapping = TextWrapping.Wrap
        });

        return new Border
        {
            MinHeight = 62,
            Margin = new Thickness(4),
            Padding = new Thickness(11, 9, 11, 9),
            Background = new SolidColorBrush(Color.FromRgb(19, 24, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(49, 59, 78)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = content
        };
    }

    private Border CreateControllerGuideCard(ControllerGuideItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = CreateControllerBadge(item.Control);
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var actionText = new TextBlock
        {
            Text = item.Action,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(231, 235, 243)),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(actionText, 1);
        grid.Children.Add(actionText);

        return new Border
        {
            MinHeight = 62,
            Margin = new Thickness(4),
            Padding = new Thickness(9, 8, 9, 8),
            Background = new SolidColorBrush(Color.FromRgb(19, 24, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(49, 59, 78)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = grid
        };
    }

    private Border CreateControllerBadge(string control)
    {
        var label = GetControllerBadgeText(control);
        var circular = label is "A" or "B" or "X" or "Y" or "LS" or "RS" or "L3" or "R3";

        return new Border
        {
            Width = circular ? 40 : 54,
            Height = 36,
            CornerRadius = new CornerRadius(circular ? 20 : 8),
            Background = new SolidColorBrush(Color.FromRgb(34, 42, 59)),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = label.Length >= 4 ? 10 : 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            }
        };
    }

    private static string GetControllerBadgeText(string control) => control switch
    {
        "A" => "A",
        "B" => "B",
        "X" => "X",
        "Y" => "Y",
        "D-Pad Up" => "D↑",
        "D-Pad Down" => "D↓",
        "D-Pad Left" => "D←",
        "D-Pad Right" => "D→",
        "LB / Left Shoulder" => "LB",
        "RB / Right Shoulder" => "RB",
        "LT / Left Trigger" => "LT",
        "RT / Right Trigger" => "RT",
        "Menu / Start" => "MENU",
        "View / Back" => "VIEW",
        "Left Stick Click / L3" => "L3",
        "Right Stick Click / R3" => "R3",
        "Left Stick" => "LS",
        "Right Stick" => "RS",
        _ => control.Length <= 5 ? control.ToUpperInvariant() : control[..5].ToUpperInvariant()
    };

    private Border CreateSetupHelperCard(string description) => new()
    {
        Margin = new Thickness(4, 0, 4, 2),
        Padding = new Thickness(12, 10, 12, 10),
        Background = new SolidColorBrush(Color.FromRgb(12, 16, 24)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(49, 59, 78)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Child = new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(204, 211, 225)),
            TextWrapping = TextWrapping.Wrap
        }
    };

    private void AddGuideFooterAction(string label, bool isEnabled, Action action)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = label,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            },
            MinHeight = 60,
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(5, 0, 5, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = isEnabled,
            BorderThickness = new Thickness(2),
            Tag = action
        };
        button.Click += ActionButton_Click;
        GuideActionPanel.Children.Add(button);
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

        if (_mode is OverlayMode.Switcher or OverlayMode.AppKiller)
        {
            _pendingAppKillerForceClose = null;
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
        var parent = _mode == OverlayMode.ControllerGuide
            ? (DependencyObject)GuideActionPanel
            : ActionPanel;
        var first = FindVisualChildren<Button>(parent)
            .FirstOrDefault(button => button.IsVisible && button.IsEnabled);
        first?.Focus();

        if (_mode == OverlayMode.ControllerGuide)
        {
            // The footer is outside the scroll region, so focus stays independent from content.
            // Explicitly keep the guide content at the top as an extra guard against WPF focus
            // bring-into-view behavior on unusually small displays.
            ContentScrollViewer.ScrollToTop();
        }
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
        IReadOnlyList<ControllerGuideItem> Controls,
        string? QuickDisableControllerProfileLabel,
        string? QuickDisableControllerProfileDescription);

    private enum OverlayMode
    {
        Home,
        Switcher,
        AppKiller,
        ControllerGuide
    }
}
