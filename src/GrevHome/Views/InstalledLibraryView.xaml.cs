using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GrevHome.Apps;
using GrevHome.Presentation;
using GrevHome.Runtime;
using GrevHome.Sessions;
using GrevHome.Store;
using GrevHome.Games;

namespace GrevHome.Views;

public partial class InstalledLibraryView : UserControl
{
    private readonly GrevStoreCatalogService _storeCatalog = new();
    private IReadOnlyList<InstalledAppEntry> _entries = Array.Empty<InstalledAppEntry>();
    private IReadOnlyList<LaunchSessionSnapshot> _runningSessions = Array.Empty<LaunchSessionSnapshot>();
    private string _filter = "All";
    private SessionUser? _primaryUser;
    private InstalledAppEntry? _actionMenuEntry;
    private GameLibraryEntry? _actionMenuGame;
    private LaunchSessionSnapshot? _actionMenuSession;
    private Button? _actionMenuOriginButton;
    private Guid? _pendingForceKillSessionId;
    private int? _pendingControllerIndex;
    private InstalledAppEntry? _pendingControllerEntry;
    private GameLibraryEntry? _pendingControllerGame;
    private Button? _pendingControllerButton;
    private bool _pendingControllerLongPress;

    public event EventHandler? BackRequested;
    public event Action<InstalledAppEntry>? LaunchRequested;
    public event Action<InstalledAppEntry>? ActionMenuLaunchRequested;
    public event Action<InstalledAppEntry>? SettingsRequested;
    public event Action<InstalledAppEntry>? StoreRequested;
    public event Action<Guid>? SwitchRequested;
    public event Action<Guid>? RestartRequested;
    public event Action<Guid>? CloseRequested;
    public event Action<Guid>? ForceKillRequested;
    public event Action<InstalledAppEntry>? AppKillerRequested;
    public event EventHandler? RunningAppsRequested;
    public event EventHandler? ActionMenuOpened;
    public event EventHandler? ActionMenuCancelRequested;

    public bool IsActionMenuOpen => AppActionOverlay.Visibility == Visibility.Visible;

    public InstalledLibraryView()
    {
        InitializeComponent();
    }

    public void SetLibrary(IReadOnlyList<InstalledAppEntry> entries, SessionUser? primaryUser)
    {
        CancelControllerAppPress();
        CloseActionMenu(returnFocus: false);
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

    public void SetRunningSessions(IReadOnlyList<LaunchSessionSnapshot> sessions)
    {
        _runningSessions = sessions;
        if (_pendingForceKillSessionId.HasValue &&
            _runningSessions.All(session => session.LaunchSessionId != _pendingForceKillSessionId.Value))
        {
            _pendingForceKillSessionId = null;
        }

        if (IsActionMenuOpen && _actionMenuEntry is not null)
        {
            UpdateActionMenuState(_actionMenuEntry);
        }
    }

    public void ShowLaunchStarted(LaunchSessionSnapshot session)
    {
        StatusText.Text = $"Started {session.AppName} • session {session.LaunchSessionId.ToString()[..8]} • PID {session.RootProcessId}. Grev Home is staying resident in the background.";
    }

    public void ShowLaunchError(string message)
    {
        StatusText.Text = $"Launch failed: {message}";
    }

    public void ShowStatus(string message)
    {
        StatusText.Text = message;
    }

    public bool BeginControllerAppPress(int controllerIndex)
    {
        if (IsActionMenuOpen || _pendingControllerIndex.HasValue)
        {
            return false;
        }

        if (Keyboard.FocusedElement is not Button button || !button.IsEnabled)
        {
            return false;
        }

        var entry = button.Tag as InstalledAppEntry;
        var game = button.Tag as GameLibraryEntry;
        if (entry is null && game is null) return false;

        _pendingControllerIndex = controllerIndex;
        _pendingControllerEntry = entry;
        _pendingControllerGame = game;
        _pendingControllerButton = button;
        _pendingControllerLongPress = false;

        // The normal controller Accept event is already queued by MainWindow. Temporarily
        // remove the tile tag so that queued click becomes a no-op while Grev Home decides
        // whether this was a short press or a long-press action-menu request.
        button.Tag = null;
        return true;
    }

    public void HandleControllerAppLongPress(int controllerIndex)
    {
        if (_pendingControllerIndex != controllerIndex || (_pendingControllerEntry is null && _pendingControllerGame is null) || _pendingControllerButton is null)
        {
            return;
        }

        _pendingControllerLongPress = true;
        RestorePendingControllerButtonTag();
        if (_pendingControllerEntry is not null) OpenActionMenu(_pendingControllerEntry, _pendingControllerButton);
        else OpenGameActionMenu(_pendingControllerGame!, _pendingControllerButton);
    }

    public void CompleteControllerAppPress(int controllerIndex)
    {
        if (_pendingControllerIndex != controllerIndex || (_pendingControllerEntry is null && _pendingControllerGame is null))
        {
            return;
        }

        var entry = _pendingControllerEntry;
        var game = _pendingControllerGame;
        var wasLongPress = _pendingControllerLongPress;
        RestorePendingControllerButtonTag();
        ClearPendingControllerPress();

        if (!wasLongPress)
        {
            if (entry is not null)
            {
                StatusText.Text = $"Starting {entry.Manifest.Definition.Name}...";
                LaunchRequested?.Invoke(entry);
            }
            else if (game is not null)
            {
                StatusText.Text = $"Starting {game.DisplayName}...";
                GameLaunchRequested?.Invoke(game);
            }
        }
    }

    public void CancelControllerAppPress(int? controllerIndex = null)
    {
        if (controllerIndex.HasValue && _pendingControllerIndex != controllerIndex)
        {
            return;
        }

        RestorePendingControllerButtonTag();
        ClearPendingControllerPress();
    }

    public void CloseActionMenu(bool returnFocus = true)
    {
        if (!IsActionMenuOpen)
        {
            return;
        }

        AppActionOverlay.Visibility = Visibility.Collapsed;
        _pendingForceKillSessionId = null;
        _actionMenuEntry = null;
        _actionMenuGame = null;
        _actionMenuSession = null;

        var origin = _actionMenuOriginButton;
        _actionMenuOriginButton = null;
        if (returnFocus && origin is { IsVisible: true, IsEnabled: true })
        {
            Dispatcher.BeginInvoke(new Action(() => origin.Focus()));
        }
    }

    public void RefocusActionMenu()
    {
        if (!IsActionMenuOpen)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(FocusFirstActionButton));
    }

    private void Render()
    {
        AppsPanel.Children.Clear();

        var visible = _entries.Where(MatchesFilter).ToArray();
        EmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _entries.Count == 0
            ? "Nothing is installed yet. Install supported packages from Grev Store."
            : "No installed apps match this filter.";

        foreach (var entry in visible)
        {
            var definition = entry.Manifest.Definition;
            var package = _storeCatalog.Find(definition.AppId);
            var displayName = package?.Presentation.DisplayName ?? definition.Name;
            var tileColor = package?.Presentation.TileColor ?? "#151923";
            var icon = package?.Presentation.IconAsset;

            var launchButton = new Button
            {
                Width = DefaultThemeMetrics.AppTileWidth,
                Height = DefaultThemeMetrics.AppTileHeight,
                Margin = new Thickness(8),
                Padding = new Thickness(0),
                Tag = entry,
                IsEnabled = entry.AvailableToCurrentUser,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = AppArtworkFactory.CreateTile(displayName, icon, tileColor)
            };
            launchButton.Click += App_Click;
            launchButton.PreviewMouseRightButtonUp += App_RightClick;
            AppsPanel.Children.Add(launchButton);
        }

        StatusText.Text = _entries.Count == 0
            ? "The Installed Library is ready for packages installed from Grev Store."
            : $"{visible.Length} shown • {_entries.Count} installed. A/Enter opens • hold A or right-click an app tile for actions.";
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
        if (sender is Button { Tag: InstalledAppEntry entry })
        {
            StatusText.Text = $"Starting {entry.Manifest.Definition.Name}...";
            LaunchRequested?.Invoke(entry);
        }
    }

    private void App_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { Tag: InstalledAppEntry entry } button && button.IsEnabled)
        {
            e.Handled = true;
            button.Focus();
            OpenActionMenu(entry, button);
        }
    }

    private void OpenActionMenu(InstalledAppEntry entry, Button originButton)
    {
        CancelControllerAppPress();
        _actionMenuEntry = entry;
        _actionMenuGame = null;
        _actionMenuOriginButton = originButton;
        _pendingForceKillSessionId = null;
        AppActionTitleText.Text = _storeCatalog.Find(entry.Manifest.Definition.AppId)?.Presentation.DisplayName
                                  ?? entry.Manifest.Definition.Name;
        AppActionOverlay.Visibility = Visibility.Visible;
        UpdateActionMenuState(entry);
        FocusFirstActionButton();
        ActionMenuOpened?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateActionMenuState(InstalledAppEntry entry)
    {
        var matching = GetMatchingSessions(entry).ToArray();
        _actionMenuSession = matching.Length == 1 ? matching[0] : null;

        AppActionSettingsButton.Visibility = Visibility.Visible;
        AppActionStoreButton.Visibility = Visibility.Visible;

        AppActionOpenButton.Visibility = matching.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AppActionSwitchButton.Visibility = matching.Length == 1 ? Visibility.Visible : Visibility.Collapsed;
        AppActionRestartButton.Visibility = matching.Length == 1 ? Visibility.Visible : Visibility.Collapsed;
        AppActionCloseButton.Visibility = matching.Length == 1 ? Visibility.Visible : Visibility.Collapsed;
        AppActionForceKillButton.Visibility = matching.Length == 1 ? Visibility.Visible : Visibility.Collapsed;
        AppActionAppKillerButton.Visibility = Visibility.Visible;
        AppActionAppKillerButton.IsEnabled = matching.Length > 0;
        AppActionRunningAppsButton.Visibility = matching.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (matching.Length == 0)
        {
            AppActionStateText.Text = "Not running • Open launches this app through the Grev Home runtime. App Killer is unavailable until Grev Home is tracking a running session for this app.";
            AppActionAppKillerButton.Content = "App Killer";
        }
        else if (matching.Length == 1)
        {
            var session = matching[0];
            AppActionStateText.Text = $"Running • {session.State} • {session.Elapsed:hh\\:mm\\:ss}";
            AppActionRestartButton.IsEnabled = session.State == LaunchSessionState.Running;
            AppActionCloseButton.IsEnabled = session.State != LaunchSessionState.Closing;
            AppActionCloseButton.Content = session.State == LaunchSessionState.Closing ? "Closing…" : "Close App";
            AppActionForceKillButton.IsEnabled = true;
            AppActionForceKillButton.Content = _pendingForceKillSessionId == session.LaunchSessionId
                ? "CONFIRM FORCE KILL APP"
                : "Force Kill App";
            AppActionAppKillerButton.Content = "App Killer";
        }
        else
        {
            AppActionStateText.Text = $"{matching.Length} managed sessions are running for this app. Use App Killer or Running Apps to choose the exact session.";
            _pendingForceKillSessionId = null;
            AppActionAppKillerButton.Content = "App Killer";
        }
    }

    private IEnumerable<LaunchSessionSnapshot> GetMatchingSessions(InstalledAppEntry entry)
    {
        var appId = entry.Manifest.Definition.AppId;
        var ownerGrevId = entry.Manifest.OwnerGrevId;

        return _runningSessions.Where(session =>
            string.Equals(session.AppId, appId, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(ownerGrevId) ||
             string.Equals(session.PrimaryGrevId, ownerGrevId, StringComparison.OrdinalIgnoreCase)));
    }

    private void FocusFirstActionButton()
    {
        var first = new[]
        {
            AppActionOpenButton,
            AppActionSwitchButton,
            AppActionSettingsButton,
            AppActionRestartButton,
            AppActionCloseButton,
            AppActionForceKillButton,
            AppActionAppKillerButton,
            AppActionRunningAppsButton,
            AppActionStoreButton,
            AppActionCancelButton
        }.FirstOrDefault(button => button.Visibility == Visibility.Visible && button.IsEnabled);

        first?.Focus();
    }

    private void RestorePendingControllerButtonTag()
    {
        if (_pendingControllerButton is not null && (_pendingControllerEntry is not null || _pendingControllerGame is not null))
        {
            _pendingControllerButton.Tag = (object?)_pendingControllerEntry ?? _pendingControllerGame;
        }
    }

    private void ClearPendingControllerPress()
    {
        _pendingControllerIndex = null;
        _pendingControllerEntry = null;
        _pendingControllerGame = null;
        _pendingControllerButton = null;
        _pendingControllerLongPress = false;
    }

    private void AppActionOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_actionMenuEntry is not null)
        {
            ActionMenuLaunchRequested?.Invoke(_actionMenuEntry);
        }
        else if (_actionMenuGame is not null)
        {
            GameLaunchRequested?.Invoke(_actionMenuGame);
        }
    }

    private void AppActionSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_actionMenuSession is not null)
        {
            SwitchRequested?.Invoke(_actionMenuSession.LaunchSessionId);
        }
    }

    private void AppActionSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_actionMenuEntry is not null)
        {
            SettingsRequested?.Invoke(_actionMenuEntry);
        }
        else if (_actionMenuGame is not null)
        {
            GameSettingsRequested?.Invoke(_actionMenuGame);
        }
    }

    private void AppActionRestart_Click(object sender, RoutedEventArgs e)
    {
        if (_actionMenuSession is not null)
        {
            RestartRequested?.Invoke(_actionMenuSession.LaunchSessionId);
        }
    }

    private void AppActionClose_Click(object sender, RoutedEventArgs e)
    {
        if (_actionMenuSession is not null)
        {
            CloseRequested?.Invoke(_actionMenuSession.LaunchSessionId);
        }
    }

    private void AppActionForceKill_Click(object sender, RoutedEventArgs e)
    {
        var session = _actionMenuSession;
        if (session is null)
        {
            return;
        }

        if (_pendingForceKillSessionId != session.LaunchSessionId)
        {
            _pendingForceKillSessionId = session.LaunchSessionId;
            AppActionForceKillButton.Content = "CONFIRM FORCE KILL APP";
            AppActionStateText.Text = "Force Kill can interrupt saves or configuration writes. Press CONFIRM FORCE KILL APP again to terminate the tracked process tree.";
            return;
        }

        _pendingForceKillSessionId = null;
        ForceKillRequested?.Invoke(session.LaunchSessionId);
    }

    private void AppActionAppKiller_Click(object sender, RoutedEventArgs e)
    {
        if (_actionMenuEntry is not null && AppActionAppKillerButton.IsEnabled)
        {
            AppKillerRequested?.Invoke(_actionMenuEntry);
        }
    }

    private void AppActionRunningApps_Click(object sender, RoutedEventArgs e) =>
        RunningAppsRequested?.Invoke(this, EventArgs.Empty);

    private void AppActionStore_Click(object sender, RoutedEventArgs e)
    {
        if (_actionMenuEntry is not null)
        {
            StoreRequested?.Invoke(_actionMenuEntry);
        }
    }

    private void AppActionCancel_Click(object sender, RoutedEventArgs e) =>
        ActionMenuCancelRequested?.Invoke(this, EventArgs.Empty);

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
