using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GrevHome.Input;
using GrevHome.Navigation;
using GrevHome.Profiles;
using GrevHome.Runtime;
using GrevHome.Sessions;
using GrevHome.Storage;
using GrevHome.Views;

namespace GrevHome.Online;

/// <summary>
/// Owns Grev Home's entire Grev.dad integration: account linking, presence/activity publishing,
/// profile-history sync, connection maintenance, friends, privacy settings and the in-shell
/// Grev.dad/general web browser. Extracted from MainWindow's eight GrevDad-prefixed partial
/// classes so this subsystem has an explicit, reviewable dependency list instead of implicit
/// access to MainWindow's full field set.
///
/// MainWindow constructs one instance early (its own constructor wires a couple of view events
/// directly to this coordinator), calls <see cref="Initialize"/> once Loaded-time services are
/// ready, and calls <see cref="Dispose"/> from its own Closed handler.
/// </summary>
public sealed partial class GrevDadCoordinator
{
    // Injected MainWindow-owned dependencies. Field names intentionally match the corresponding
    // MainWindow field so the method bodies below - moved verbatim from the former
    // MainWindow.GrevDad*.cs partial files - did not need to change beyond that rename.
    private readonly AppPaths _paths;
    private readonly SessionContext _session;
    private readonly NavigationService _navigation;
    private readonly RuntimeSessionManager _runtimeSessions;
    private readonly ProfileService _profileService;
    private readonly Dispatcher _dispatcher;
    private readonly DashboardView _dashboardView;
    private readonly ProfileEditView _profileEditView;
    private readonly CreateProfileView _createProfileView;
    private readonly ProfileView _profileView;
    private readonly ContentControl _routeHost;
    private readonly Button _shellFriendsButton;
    private readonly Func<LocalProfile?> _getProfileTarget;
    private readonly Func<Task> _refreshLoginProfileDetailsAsync;
    private readonly Func<string, Task> _loadProfileStatsAsync;
    private readonly Action _returnToLogin;

    public GrevDadCoordinator(
        AppPaths paths,
        SessionContext session,
        NavigationService navigation,
        RuntimeSessionManager runtimeSessions,
        ProfileService profileService,
        Dispatcher dispatcher,
        DashboardView dashboardView,
        ProfileEditView profileEditView,
        CreateProfileView createProfileView,
        ProfileView profileView,
        ContentControl routeHost,
        Button shellFriendsButton,
        Func<LocalProfile?> getProfileTarget,
        Func<Task> refreshLoginProfileDetailsAsync,
        Func<string, Task> loadProfileStatsAsync,
        Action returnToLogin)
    {
        _paths = paths;
        _session = session;
        _navigation = navigation;
        _runtimeSessions = runtimeSessions;
        _profileService = profileService;
        _dispatcher = dispatcher;
        _dashboardView = dashboardView;
        _profileEditView = profileEditView;
        _createProfileView = createProfileView;
        _profileView = profileView;
        _routeHost = routeHost;
        _shellFriendsButton = shellFriendsButton;
        _getProfileTarget = getProfileTarget;
        _refreshLoginProfileDetailsAsync = refreshLoginProfileDetailsAsync;
        _loadProfileStatsAsync = loadProfileStatsAsync;
        _returnToLogin = returnToLogin;
    }

    private SessionHistoryService? _sessionHistory;

    /// <summary>
    /// Wires every Grev.dad integration. Call once, after Loaded-time services (in particular the
    /// local session-history service) are ready - mirrors the order MainWindow previously called
    /// its five separate InitializeGrevDad*Integration() methods in.
    /// </summary>
    public void Initialize(SessionHistoryService sessionHistory)
    {
        _sessionHistory = sessionHistory;
        InitializeGrevDadIntegration();
        InitializeGrevDadMaintenanceIntegration();
        InitializeGrevDadProfileSyncIntegration();
        InitializeGrevDadSettingsIntegration();
        InitializeGrevDadPrivacySettingsUiIntegration();
    }

    /// <summary>
    /// Stops every timer and disposes every disposable service owned by this coordinator. Call
    /// from MainWindow's own Closed handler. Consolidates what were five separate Closed
    /// subscriptions across the former MainWindow.GrevDad*.cs partial files.
    /// </summary>
    public void Dispose()
    {
        _messageTimer.Stop();
        _messageGeneration++;
        _grevDadWebView.Dispose();

        _grevDadHeartbeatTimer.Stop();
        _grevDadAccounts?.Dispose();

        _grevDadMaintenanceTimer.Stop();
        _grevDadMaintenance?.Dispose();

        _grevDadSyncRetryTimer.Stop();
        _grevDadSyncRetries.Clear();
        _grevDadProfileSync?.Dispose();

        _grevDadLinkPollTimer.Stop();
    }

    /// <summary>True while the in-shell Grev.dad/general web browser owns controller input.</summary>
    public bool WebOwnsControllerInput => _grevDadWebView.OwnsControllerInput;

    /// <summary>Forwards a controller action to the in-shell web browser while it is the active route.</summary>
    public bool HandleWebInput(InputAction action) => _grevDadWebView.HandleInput(action);

    /// <summary>
    /// Saves a profile's public presentation card to Grev.dad only when this GrevID is currently
    /// linked. No-ops otherwise (mirrors the profile-presentation save flow's original inline check).
    /// </summary>
    public async Task SavePublicCardIfLinkedAsync(string grevId, GrevDadPublicCard card)
    {
        var accounts = _grevDadAccounts;
        if (accounts is null)
        {
            return;
        }

        var link = accounts.GetLastSnapshot(grevId);
        if (link.State == GrevDadConnectionState.Linked)
        {
            var profile = (await _profileService.GetProfilesAsync()).FirstOrDefault(item => item.GrevId == grevId);
            if (profile is not null) card = card with { Bio = profile.Bio, StatusMessage = profile.StatusMessage };
            await accounts.SavePublicCardAsync(grevId, card);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Formerly MainWindow.GrevDad.cs - account/presence/activity core.
    // ---------------------------------------------------------------------------------------

    private readonly ConcurrentDictionary<string, byte> _grevDadPublishedRuntimeSessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _grevDadHeartbeatTimer = new()
    {
        Interval = TimeSpan.FromMinutes(2)
    };
    private HashSet<string> _lastSignedInGrevDadGrevIds = new(StringComparer.OrdinalIgnoreCase);

    private GrevDadAccountService? _grevDadAccounts;
    private GrevDadPrivacySettingsService? _grevDadPrivacy;
    private bool _grevDadIntegrationReady;

    private void InitializeGrevDadIntegration()
    {
        if (_grevDadIntegrationReady)
        {
            return;
        }

        _grevDadIntegrationReady = true;
        _grevDadAccounts = new GrevDadAccountService(_paths);
        _grevDadPrivacy = new GrevDadPrivacySettingsService(_paths);
        InitializeGrevDadFriendsIntegration();
        InitializeGrevDadWebIntegration();

        _session.Changed += (_, _) => _dispatcher.BeginInvoke(new Action(() => _ = HandleGrevDadSessionChangedAsync()));
        _runtimeSessions.SessionChanged += HandleGrevDadRuntimeChanged;
        _runtimeSessions.SessionEnded += HandleGrevDadRuntimeEnded;
        _grevDadHeartbeatTimer.Tick += (_, _) => _ = RefreshGrevDadPresenceHeartbeatAsync();

        _ = HandleGrevDadSessionChangedAsync();
    }

    private GrevDadAccountService RequireGrevDadAccountService() =>
        _grevDadAccounts
        ?? throw new InvalidOperationException("Grev.dad account services have not been initialized.");

    private GrevDadPrivacySettingsService RequireGrevDadPrivacySettingsService() =>
        _grevDadPrivacy
        ?? throw new InvalidOperationException("Grev.dad privacy services have not been initialized.");

    private async Task HandleGrevDadSessionChangedAsync()
    {
        var service = _grevDadAccounts;
        if (service is null)
        {
            return;
        }

        var current = _session.SignedInUsers
            .Select(user => user.GrevId)
            .Where(grevId => !string.IsNullOrWhiteSpace(grevId))
            .Select(grevId => grevId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = _lastSignedInGrevDadGrevIds
            .Where(grevId => !current.Contains(grevId))
            .ToArray();
        _lastSignedInGrevDadGrevIds = current;

        foreach (var grevId in removed)
        {
            _ = SetGrevDadPresenceSafeAsync(grevId, "offline", "none", "", expiresInSeconds: 60);
        }

        var activeParticipantGrevIds = GetAllActiveRuntimeGrevIds();
        if (current.Count == 0 && activeParticipantGrevIds.Count == 0)
        {
            _grevDadHeartbeatTimer.Stop();
            return;
        }

        foreach (var grevId in current.Concat(activeParticipantGrevIds).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var local = await service.LoadLocalStateAsync(grevId);
                if (local.State == GrevDadConnectionState.Linked)
                {
                    var validated = await service.ValidateLinkedAccountAsync(grevId);
                    if (validated.State is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline)
                    {
                        _grevDadHeartbeatTimer.Start();
                        await RefreshGrevDadPresenceForAsync(grevId);
                    }
                }
                else if (local.State == GrevDadConnectionState.Offline)
                {
                    _grevDadHeartbeatTimer.Start();
                }
            }
            catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
            {
                // Grev.dad is additive. One participant's online-account failure must never affect
                // any other local GrevID, controller assignment or runtime session.
            }
        }
    }

    private void HandleGrevDadRuntimeChanged(LaunchSessionSnapshot snapshot)
    {
        if (snapshot.State != LaunchSessionState.Running)
        {
            return;
        }

        foreach (var grevId in GetPersistentParticipantGrevIds(snapshot))
        {
            var key = BuildRuntimePublicationKey(snapshot.LaunchSessionId, grevId);
            if (_grevDadPublishedRuntimeSessions.TryAdd(key, 0))
            {
                _ = PublishGrevDadRuntimeActivitySafeAsync(snapshot, grevId, started: true);
            }
        }
    }

    private void HandleGrevDadRuntimeEnded(LaunchSessionSnapshot snapshot)
    {
        foreach (var grevId in GetPersistentParticipantGrevIds(snapshot))
        {
            var key = BuildRuntimePublicationKey(snapshot.LaunchSessionId, grevId);
            if (_grevDadPublishedRuntimeSessions.TryRemove(key, out _))
            {
                _ = PublishGrevDadRuntimeActivitySafeAsync(snapshot, grevId, started: false);
            }
            else
            {
                _ = RefreshGrevDadPresenceForAsync(grevId);
            }
        }
    }

    private async Task PublishGrevDadRuntimeActivitySafeAsync(
        LaunchSessionSnapshot snapshot,
        string grevId,
        bool started)
    {
        var service = _grevDadAccounts;
        var privacyService = _grevDadPrivacy;
        if (service is null || privacyService is null || string.IsNullOrWhiteSpace(grevId))
        {
            return;
        }

        try
        {
            var local = service.GetLastSnapshot(grevId);
            if (local.State == GrevDadConnectionState.Unlinked)
            {
                local = await service.LoadLocalStateAsync(grevId);
            }

            if (local.State is not (GrevDadConnectionState.Linked or GrevDadConnectionState.Offline))
            {
                return;
            }

            var privacy = await privacyService.GetAsync(grevId);
            if (privacy.ShareLiveActivityEvents)
            {
                await service.PublishAppActivityAsync(
                    grevId,
                    started,
                    snapshot.AppId,
                    snapshot.AppName,
                    detail: started ? "Started from Grev Home" : "Stopped in Grev Home",
                    visibility: privacy.ActivityVisibility);
            }

            await RefreshGrevDadPresenceForAsync(grevId);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            // Presence/activity delivery must never affect app launch, runtime tracking or playtime.
        }
    }

    private async Task RefreshGrevDadPresenceHeartbeatAsync()
    {
        var grevIds = GetAllActiveRuntimeGrevIds()
            .Concat(_session.SignedInUsers
                .Select(user => user.GrevId)
                .Where(grevId => !string.IsNullOrWhiteSpace(grevId))
                .Select(grevId => grevId!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (grevIds.Length == 0)
        {
            _grevDadHeartbeatTimer.Stop();
            return;
        }

        foreach (var grevId in grevIds)
        {
            await RefreshGrevDadPresenceForAsync(grevId);
        }
    }

    private async Task RefreshGrevDadPresenceForAsync(string grevId)
    {
        var service = _grevDadAccounts;
        var privacyService = _grevDadPrivacy;
        if (service is null || privacyService is null)
        {
            return;
        }

        try
        {
            var local = service.GetLastSnapshot(grevId);
            if (local.State == GrevDadConnectionState.Unlinked)
            {
                local = await service.LoadLocalStateAsync(grevId);
            }

            if (local.State is not (GrevDadConnectionState.Linked or GrevDadConnectionState.Offline))
            {
                return;
            }

            var privacy = await privacyService.GetAsync(grevId);
            if (!privacy.SharePresence)
            {
                await service.UpdatePresenceAsync(
                    grevId,
                    "offline",
                    "none",
                    "",
                    expiresInSeconds: 60);
                return;
            }

            var active = _runtimeSessions.GetActiveSessions()
                .Where(session => GetPersistentParticipantGrevIds(session).Contains(grevId, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(session => session.StartedAtUtc)
                .FirstOrDefault();
            var signedIn = _session.SignedInUsers.Any(user =>
                string.Equals(user.GrevId, grevId, StringComparison.OrdinalIgnoreCase));

            if (active is not null && privacy.SharePlayingStatus)
            {
                await service.UpdatePresenceAsync(
                    grevId,
                    "online",
                    "playing",
                    active.AppName,
                    expiresInSeconds: 300);
            }
            else if (active is not null || signedIn)
            {
                await service.UpdatePresenceAsync(
                    grevId,
                    "online",
                    "none",
                    "",
                    expiresInSeconds: 300);
            }
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            // Heartbeats are best effort and expire server-side. Never block the local appliance.
        }
    }

    private async Task SetGrevDadPresenceSafeAsync(
        string grevId,
        string availability,
        string activityType,
        string activityText,
        int expiresInSeconds)
    {
        var service = _grevDadAccounts;
        if (service is null)
        {
            return;
        }

        try
        {
            await service.UpdatePresenceAsync(
                grevId,
                availability,
                activityType,
                activityText,
                expiresInSeconds: expiresInSeconds);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
        }
    }

    private IReadOnlyList<string> GetAllActiveRuntimeGrevIds() =>
        _runtimeSessions.GetActiveSessions()
            .SelectMany(GetPersistentParticipantGrevIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> GetPersistentParticipantGrevIds(LaunchSessionSnapshot snapshot) =>
        snapshot.Participants
            .Select(participant => participant.GrevId)
            .Where(grevId => !string.IsNullOrWhiteSpace(grevId))
            .Select(grevId => grevId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildRuntimePublicationKey(Guid sessionId, string grevId) =>
        $"{sessionId:N}:{grevId}";

    private static bool IsExpectedGrevDadBackgroundFailure(Exception ex) =>
        ex is HttpRequestException or OperationCanceledException or TimeoutException or InvalidOperationException or
            InvalidDataException or UnauthorizedAccessException or Win32Exception;

    // ---------------------------------------------------------------------------------------
    // Formerly MainWindow.GrevDadMaintenance.cs - periodic capability/link maintenance.
    // ---------------------------------------------------------------------------------------

    private readonly DispatcherTimer _grevDadMaintenanceTimer = new()
    {
        Interval = TimeSpan.FromHours(6)
    };
    private GrevDadConnectionMaintenanceService? _grevDadMaintenance;
    private bool _grevDadMaintenanceReady;
    private int _grevDadMaintenanceActive;

    private void InitializeGrevDadMaintenanceIntegration()
    {
        if (_grevDadMaintenanceReady)
        {
            return;
        }

        _grevDadMaintenanceReady = true;
        _grevDadMaintenance = new GrevDadConnectionMaintenanceService(
            _paths,
            RequireGrevDadAccountService());

        _grevDadMaintenanceTimer.Tick += (_, _) => _ = MaintainAllGrevDadLinksSafeAsync();
        _grevDadMaintenanceTimer.Start();

        // Read profiles independently rather than assuming Loaded/profile enumeration has already
        // finished. Online maintenance must not introduce an ordering dependency into shell startup.
        _dispatcher.BeginInvoke(new Action(() => _ = MaintainAllGrevDadLinksSafeAsync()), DispatcherPriority.Background);
    }

    private async Task MaintainAllGrevDadLinksSafeAsync()
    {
        var maintenance = _grevDadMaintenance;
        if (maintenance is null || Interlocked.Exchange(ref _grevDadMaintenanceActive, 1) != 0)
        {
            return;
        }

        try
        {
            IReadOnlyList<LocalProfile> profiles;
            try
            {
                profiles = await _profileService.GetProfilesAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return;
            }

            foreach (var profile in profiles)
            {
                try
                {
                    await maintenance.MaintainLinkedProfileAsync(profile);
                }
                catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) ||
                                           ex is IOException or UnauthorizedAccessException)
                {
                    // Capability refresh/rotation is maintenance only. A failure cannot affect the
                    // local profile, sign-in, app runtime or any other linked GrevID on the machine.
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _grevDadMaintenanceActive, 0);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Formerly MainWindow.GrevDadSync.cs - local session-history to Grev.dad sync, with retry.
    // ---------------------------------------------------------------------------------------

    private sealed record GrevDadSyncRetryState(int FailureCount, DateTimeOffset NextAttemptAtUtc);

    private static readonly TimeSpan[] GrevDadSyncRetryBackoff =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30)
    ];

    private readonly ConcurrentDictionary<string, GrevDadSyncRetryState> _grevDadSyncRetries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _grevDadSyncRetryTimer = new()
    {
        Interval = TimeSpan.FromSeconds(5)
    };

    private GrevDadProfileSyncService? _grevDadProfileSync;
    private bool _grevDadProfileSyncReady;
    private int _grevDadSyncRetryTickActive;

    private void InitializeGrevDadProfileSyncIntegration()
    {
        if (_grevDadProfileSyncReady)
        {
            return;
        }

        var history = _sessionHistory
            ?? throw new InvalidOperationException("Local session history must initialize before Grev.dad sync.");
        var accounts = RequireGrevDadAccountService();
        var privacy = RequireGrevDadPrivacySettingsService();

        _grevDadProfileSyncReady = true;
        _grevDadProfileSync = new GrevDadProfileSyncService(_paths, history, _profileService, accounts, privacy);
        _grevDadSyncRetryTimer.Tick += (_, _) => _ = RetryDueGrevDadSyncsAsync();

        // Session changes are an explicit lifecycle edge. They can backfill a linked Primary GrevID
        // after Grev Home starts without coupling sync to account SnapshotChanged; routine account
        // revalidation itself publishes snapshots and must never recursively schedule another sync.
        _session.Changed += (_, _) =>
        {
            var grevId = _session.PrimaryUser?.GrevId;
            if (!string.IsNullOrWhiteSpace(grevId))
            {
                _ = SyncGrevDadProfileSafeAsync(grevId);
            }
        };
    }

    /// <summary>
    /// Queues a Grev.dad sync for every persistent participant of a snapshot whose local session
    /// history just committed. Subscribed by MainWindow to RuntimeSessionManager.SessionHistoryCommitted.
    /// </summary>
    public void QueueSyncAfterLocalHistory(LaunchSessionSnapshot snapshot)
    {
        var grevIds = snapshot.Participants
            .Select(participant => participant.GrevId)
            .Where(grevId => !string.IsNullOrWhiteSpace(grevId))
            .Select(grevId => grevId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var grevId in grevIds)
        {
            _ = SyncGrevDadProfileSafeAsync(grevId);
        }
    }

    private async Task SyncGrevDadProfileSafeAsync(string grevId)
    {
        var sync = _grevDadProfileSync;
        var accounts = _grevDadAccounts;
        if (sync is null || accounts is null || string.IsNullOrWhiteSpace(grevId))
        {
            return;
        }

        try
        {
            var result = await sync.SyncAsync(grevId);
            if (result is not null)
            {
                if (result.HasMoreHistory)
                {
                    ScheduleGrevDadSyncContinuation(grevId);
                }
                else
                {
                    // Refresh other devices' account statistics without requiring a restart.
                    _grevDadSyncRetries[grevId] = new GrevDadSyncRetryState(0,DateTimeOffset.UtcNow+TimeSpan.FromSeconds(30));
                    EnsureGrevDadSyncRetryTimerRunning();
                }
                await _refreshLoginProfileDetailsAsync();
                if (string.Equals(_getProfileTarget()?.GrevId,grevId,StringComparison.OrdinalIgnoreCase))
                    await _loadProfileStatsAsync(grevId);
                return;
            }

            // ValidateLinkedAccountAsync deliberately converts transport failures to Offline rather
            // than throwing. Preserve eventual delivery by retrying only that state. Unlinked,
            // expired and revoked profiles must never be kept alive by a background retry loop.
            var state = accounts.GetLastSnapshot(grevId).State;
            if (state == GrevDadConnectionState.Offline)
            {
                ScheduleGrevDadSyncRetry(grevId);
            }
            else if (state is GrevDadConnectionState.Unlinked or
                     GrevDadConnectionState.Expired or
                     GrevDadConnectionState.Revoked or
                     GrevDadConnectionState.Error)
            {
                ClearGrevDadSyncRetry(grevId);
            }
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) ||
                                   ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            // The local journal remains the durable queue. Failed transport never advances its
            // cursor; a bounded backoff retries linked/offline profiles without blocking the shell.
            ScheduleGrevDadSyncRetry(grevId);
        }
    }

    private void ScheduleGrevDadSyncContinuation(string grevId)
    {
        // A successful run is intentionally capped at 1,000 history rows. If more local history
        // remains, schedule another bounded pass without treating healthy backlog as a failure.
        _grevDadSyncRetries[grevId] = new GrevDadSyncRetryState(
            0,
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));
        EnsureGrevDadSyncRetryTimerRunning();
    }

    private void ScheduleGrevDadSyncRetry(string grevId)
    {
        _grevDadSyncRetries.AddOrUpdate(
            grevId,
            _ => new GrevDadSyncRetryState(1, DateTimeOffset.UtcNow + GrevDadSyncRetryBackoff[0]),
            (_, existing) =>
            {
                var failures = Math.Min(existing.FailureCount + 1, GrevDadSyncRetryBackoff.Length);
                var delay = GrevDadSyncRetryBackoff[Math.Min(failures - 1, GrevDadSyncRetryBackoff.Length - 1)];
                return new GrevDadSyncRetryState(failures, DateTimeOffset.UtcNow + delay);
            });

        EnsureGrevDadSyncRetryTimerRunning();
    }

    private void ClearGrevDadSyncRetry(string grevId)
    {
        _grevDadSyncRetries.TryRemove(grevId, out _);
        if (_grevDadSyncRetries.IsEmpty)
        {
            StopGrevDadSyncRetryTimer();
        }
    }

    private async Task RetryDueGrevDadSyncsAsync()
    {
        if (Interlocked.Exchange(ref _grevDadSyncRetryTickActive, 1) != 0)
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var due = _grevDadSyncRetries
                .Where(pair => pair.Value.NextAttemptAtUtc <= now)
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var grevId in due)
            {
                await SyncGrevDadProfileSafeAsync(grevId);
            }

            if (_grevDadSyncRetries.IsEmpty)
            {
                StopGrevDadSyncRetryTimer();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _grevDadSyncRetryTickActive, 0);
        }
    }

    private void EnsureGrevDadSyncRetryTimerRunning()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(new Action(EnsureGrevDadSyncRetryTimerRunning));
            return;
        }

        if (!_grevDadSyncRetryTimer.IsEnabled)
        {
            _grevDadSyncRetryTimer.Start();
        }
    }

    private void StopGrevDadSyncRetryTimer()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(new Action(StopGrevDadSyncRetryTimer));
            return;
        }

        if (_grevDadSyncRetryTimer.IsEnabled)
        {
            _grevDadSyncRetryTimer.Stop();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Formerly MainWindow.GrevDadSettings.cs - account linking UI (Edit Profile / onboarding).
    // ---------------------------------------------------------------------------------------

    private readonly Dictionary<string, GrevDadLinkStart> _activeGrevDadLinks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _grevDadLinkPollTimer = new();
    private bool _grevDadSettingsIntegrationReady;
    private string? _grevDadPollingGrevId;
    private DateTimeOffset _grevDadUnlinkArmedUntilUtc;

    private void InitializeGrevDadSettingsIntegration()
    {
        if (_grevDadSettingsIntegrationReady)
        {
            return;
        }

        _grevDadSettingsIntegrationReady = true;
        _grevDadLinkPollTimer.Interval = TimeSpan.FromSeconds(3);
        _grevDadLinkPollTimer.Tick += (_, _) => _ = PollActiveGrevDadLinkAsync();

        _profileEditView.LinkGrevDadRequested += (_, _) => _ = BeginGrevDadLinkFromProfileAsync();
        _profileEditView.CheckGrevDadLinkRequested += (_, _) => _ = PollActiveGrevDadLinkAsync(forceCurrentTarget: true);
        _profileEditView.CancelGrevDadLinkRequested += (_, _) => _ = CancelGrevDadLinkFromProfileAsync();
        _profileEditView.UnlinkGrevDadRequested += (_, _) => _ = UnlinkGrevDadFromProfileAsync();
        _profileEditView.OpenGrevDadApprovalRequested += OpenGrevDadApprovalPage;
        _profileEditView.OpenGrevDadWebsiteRequested += (_,_)=>OpenGrevDadWebsite(new Uri(RequireGrevDadAccountService().BaseUri,"link-grev-home"));
        _createProfileView.OpenGrevDadRequested += profile=>OpenGrevDadWebsite(profile,new Uri(RequireGrevDadAccountService().BaseUri,"login?next=%2Flink-grev-home"));
        _createProfileView.GenerateGrevDadCodeRequested += profile=>_ = BeginGrevDadLinkFromOnboardingAsync(profile);
        _createProfileView.OpenGrevDadApprovalRequested += (profile,link)=>OpenGrevDadWebsite(profile,link.VerificationUri);
        _createProfileView.CheckGrevDadApprovalRequested += profile=>_ = CheckGrevDadLinkFromOnboardingAsync(profile);

        var service = RequireGrevDadAccountService();
        service.SnapshotChanged += (grevId, snapshot) => _dispatcher.BeginInvoke(new Action(() =>
        {
            var profile = _getProfileTarget();
            if (profile is null || !string.Equals(profile.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_navigation.Current == Route.ProfileEdit)
            {
                _profileEditView.SetGrevDadContext(profile, CanManageGrevDadProfile(profile));
                _activeGrevDadLinks.TryGetValue(grevId, out var link);
                _profileEditView.SetGrevDadState(snapshot, link);
            }
            else if (_navigation.Current == Route.ProfileView)
            {
                _profileView.SetGrevDadState(snapshot);
            }
        }));

        _navigation.RouteChanged += route =>
        {
            if (route is Route.ProfileEdit or Route.ProfileView)
            {
                _ = RefreshGrevDadProfileAsync(validateRemote: route == Route.ProfileEdit);
            }
            else
            {
                StopGrevDadLinkPolling();
            }
        };
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current is Route.ProfileEdit or Route.ProfileView)
            {
                _dispatcher.BeginInvoke(new Action(() => _ = RefreshGrevDadProfileAsync(validateRemote: false)));
            }
        };
    }

    private bool CanManageGrevDadProfile(LocalProfile profile) =>
        !string.IsNullOrWhiteSpace(_session.PrimaryUser?.GrevId) &&
        string.Equals(_session.PrimaryUser!.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase);

    private async Task RefreshGrevDadProfileAsync(bool validateRemote)
    {
        var route = _navigation.Current;
        var profile = _getProfileTarget();
        if (route == Route.ProfileEdit)
        {
            _profileEditView.SetGrevDadContext(profile, profile is not null && CanManageGrevDadProfile(profile));
        }

        if (profile is null)
        {
            if (route == Route.ProfileView)
            {
                _profileView.SetGrevDadState(GrevDadAccountSnapshot.Unlinked);
            }
            StopGrevDadLinkPolling();
            return;
        }

        var service = RequireGrevDadAccountService();
        try
        {
            var snapshot = await service.LoadLocalStateAsync(profile.GrevId);
            if (validateRemote && snapshot.State == GrevDadConnectionState.Linked)
            {
                snapshot = await service.ValidateLinkedAccountAsync(profile.GrevId);
            }

            if (_navigation.Current != route ||
                !string.Equals(_getProfileTarget()?.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (route == Route.ProfileEdit)
            {
                _activeGrevDadLinks.TryGetValue(profile.GrevId, out var link);
                _profileEditView.SetGrevDadState(snapshot, link);

                if (snapshot.State == GrevDadConnectionState.Linking && CanManageGrevDadProfile(profile))
                {
                    StartGrevDadLinkPolling(profile.GrevId, link?.PollIntervalSeconds ?? 3);
                }
                else
                {
                    StopGrevDadLinkPolling();
                }
            }
            else if (route == Route.ProfileView)
            {
                _profileView.SetGrevDadState(snapshot);
                StopGrevDadLinkPolling();
            }
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            var cached = service.GetLastSnapshot(profile.GrevId) with
            {
                State = service.GetLastSnapshot(profile.GrevId).Account is null
                    ? GrevDadConnectionState.Error
                    : GrevDadConnectionState.Offline,
                Message = ex.Message
            };

            if (route == Route.ProfileEdit)
            {
                _profileEditView.SetGrevDadState(cached);
            }
            else if (route == Route.ProfileView)
            {
                _profileView.SetGrevDadState(cached);
            }
        }
    }

    private async Task BeginGrevDadLinkFromProfileAsync()
    {
        var profile = _getProfileTarget();
        if (_navigation.Current != Route.ProfileEdit || profile is null || !CanManageGrevDadProfile(profile))
        {
            _profileEditView.ShowGrevDadStatus("This profile must be the current Primary User before its Grev.dad account link can be changed.");
            return;
        }

        var service = RequireGrevDadAccountService();
        var maintenance = _grevDadMaintenance;
        if (maintenance is null)
        {
            _profileEditView.ShowGrevDadStatus("Grev.dad integration is still initializing. Try Link Grev.dad again.");
            return;
        }

        try
        {
            _profileEditView.ShowGrevDadStatus("Checking the live Grev.dad integration contract…");
            var capabilities = await maintenance.GetCapabilitiesAsync(forceRefresh: true);
            if (!capabilities.Capabilities.Linking || !capabilities.Capabilities.DeviceTokens)
            {
                _profileEditView.ShowGrevDadStatus(
                    $"Grev.dad {capabilities.Environment} API {capabilities.ApiVersion} is online but does not advertise the required linking and device-token capabilities.");
                return;
            }

            _profileEditView.ShowGrevDadStatus(
                $"Grev.dad {capabilities.Environment} API {capabilities.ApiVersion} is ready. Creating a secure link request…");
            var link = await service.BeginLinkAsync(profile, Environment.MachineName);
            _activeGrevDadLinks[profile.GrevId] = link;
            _profileEditView.SetGrevDadState(service.GetLastSnapshot(profile.GrevId), link);
            _profileEditView.ShowGrevDadStatus(
                $"Approve code {link.UserCode} on Grev.dad. Grev Home will check automatically while Edit Profile remains open.");
            StartGrevDadLinkPolling(profile.GrevId, link.PollIntervalSeconds);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) || ex is ArgumentException)
        {
            _profileEditView.ShowGrevDadStatus(
                $"Grev.dad linking is not ready on {service.BaseUri.Host}: {ex.Message}");
        }
    }

    private async Task BeginGrevDadLinkFromOnboardingAsync(LocalProfile profile)
    {
        if (_navigation.Current != Route.CreateProfile) return;
        var service = RequireGrevDadAccountService();
        if (_grevDadMaintenance is null)
        {
            _createProfileView.ShowGrevDadOnboardingStatus("Grev.dad integration is still initializing. Try again in a moment.");
            return;
        }
        try
        {
            _createProfileView.ShowGrevDadOnboardingStatus("Checking the live Grev.dad connection…");
            var capabilities = await _grevDadMaintenance.GetCapabilitiesAsync(forceRefresh:true);
            if (!capabilities.Capabilities.Linking || !capabilities.Capabilities.DeviceTokens)
            {
                _createProfileView.ShowGrevDadOnboardingStatus("Grev.dad is online but linking is not currently available. You can skip and link later.");
                return;
            }
            var link = await service.BeginLinkAsync(profile,Environment.MachineName);
            _activeGrevDadLinks[profile.GrevId] = link;
            _createProfileView.ShowGrevDadCode(link);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) || ex is ArgumentException)
        {
            _createProfileView.ShowGrevDadOnboardingStatus($"Could not generate a link code: {ex.Message} You can skip and link later.");
        }
    }

    private async Task CheckGrevDadLinkFromOnboardingAsync(LocalProfile profile)
    {
        if (_navigation.Current != Route.CreateProfile) return;
        try
        {
            var result = await RequireGrevDadAccountService().PollLinkAsync(profile.GrevId);
            switch (result.State)
            {
                case GrevDadLinkPollState.Pending:
                    _createProfileView.ShowGrevDadOnboardingStatus("Still waiting for approval on Grev.dad. Approve the request there, then check again.");
                    return;
                case GrevDadLinkPollState.Approved:
                    _activeGrevDadLinks.Remove(profile.GrevId);
                    _createProfileView.ShowGrevDadLinked($"@{result.Account?.Username}");
                    await SyncGrevDadProfileSafeAsync(profile.GrevId);
                    return;
                case GrevDadLinkPollState.Denied:
                    _createProfileView.ShowGrevDadOnboardingStatus("The request was denied on Grev.dad. You can skip and link later from Edit Profile.");
                    return;
                case GrevDadLinkPollState.Expired:
                case GrevDadLinkPollState.Revoked:
                    _activeGrevDadLinks.Remove(profile.GrevId);
                    _createProfileView.ShowGrevDadOnboardingStatus("That request is no longer valid. Skip for now and link later from Edit Profile.");
                    return;
            }
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            _createProfileView.ShowGrevDadOnboardingStatus($"Could not check approval: {ex.Message} Your local account is safe and you can skip for now.");
        }
    }

    /// <summary>Formerly SkipGrevDadOnboardingAsync. Subscribed by MainWindow to CreateProfileView.OnboardingSkipped.</summary>
    public async Task SkipOnboardingAsync(LocalProfile profile)
    {
        try
        {
            if (_activeGrevDadLinks.Remove(profile.GrevId))
                await RequireGrevDadAccountService().CancelPendingLinkAsync(profile.GrevId);
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            // The local account is complete regardless. Expired server requests cannot link
            // without explicit approval and are cleaned up by the normal request lifecycle.
        }
        finally { _returnToLogin(); }
    }

    private async Task PollActiveGrevDadLinkAsync(bool forceCurrentTarget = false)
    {
        var profile = _getProfileTarget();
        if (_navigation.Current != Route.ProfileEdit || profile is null || !CanManageGrevDadProfile(profile))
        {
            StopGrevDadLinkPolling();
            return;
        }

        var grevId = forceCurrentTarget ? profile.GrevId : _grevDadPollingGrevId;
        if (string.IsNullOrWhiteSpace(grevId) ||
            !string.Equals(profile.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
        {
            StopGrevDadLinkPolling();
            return;
        }

        var service = RequireGrevDadAccountService();
        try
        {
            var result = await service.PollLinkAsync(grevId);
            _activeGrevDadLinks.TryGetValue(grevId, out var link);
            _profileEditView.SetGrevDadState(service.GetLastSnapshot(grevId), link);

            switch (result.State)
            {
                case GrevDadLinkPollState.Pending:
                    return;
                case GrevDadLinkPollState.Approved:
                    _activeGrevDadLinks.Remove(grevId);
                    StopGrevDadLinkPolling();
                    var approved = service.GetLastSnapshot(grevId);
                    _profileEditView.SetGrevDadState(approved);
                    _profileEditView.ShowGrevDadStatus($"Linked @{result.Account?.Username} to this GrevID profile.");
                    _ = SyncGrevDadProfileSafeAsync(grevId);
                    await RefreshGrevDadPresenceForAsync(grevId);
                    return;
                case GrevDadLinkPollState.Denied:
                    _profileEditView.ShowGrevDadStatus("The Grev.dad link request was denied.");
                    break;
                case GrevDadLinkPollState.Expired:
                    _profileEditView.ShowGrevDadStatus("The Grev.dad link request expired. Start a new request when ready.");
                    break;
                case GrevDadLinkPollState.Revoked:
                    _profileEditView.ShowGrevDadStatus("The Grev.dad link request is no longer valid.");
                    break;
            }

            _activeGrevDadLinks.Remove(grevId);
            StopGrevDadLinkPolling();
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            if (forceCurrentTarget)
            {
                _profileEditView.ShowGrevDadStatus($"Could not check Grev.dad approval: {ex.Message}");
            }
        }
    }

    private async Task CancelGrevDadLinkFromProfileAsync()
    {
        var profile = _getProfileTarget();
        if (_navigation.Current != Route.ProfileEdit || profile is null || !CanManageGrevDadProfile(profile)) return;

        try
        {
            await RequireGrevDadAccountService().CancelPendingLinkAsync(profile.GrevId);
            _activeGrevDadLinks.Remove(profile.GrevId);
            StopGrevDadLinkPolling();
            _profileEditView.SetGrevDadState(GrevDadAccountSnapshot.Unlinked);
            _profileEditView.ShowGrevDadStatus("Grev.dad link request cancelled locally for this profile.");
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            _profileEditView.ShowGrevDadStatus($"Could not cancel the Grev.dad link request: {ex.Message}");
        }
    }

    private async Task UnlinkGrevDadFromProfileAsync()
    {
        var profile = _getProfileTarget();
        if (_navigation.Current != Route.ProfileEdit || profile is null || !CanManageGrevDadProfile(profile)) return;

        var current = DateTimeOffset.UtcNow;
        if (current > _grevDadUnlinkArmedUntilUtc)
        {
            _grevDadUnlinkArmedUntilUtc = current.AddSeconds(8);
            _profileEditView.ShowGrevDadStatus(
                "Unlink armed. Select Unlink Grev.dad again within 8 seconds. The local GrevID profile will remain intact.");
            return;
        }

        _grevDadUnlinkArmedUntilUtc = DateTimeOffset.MinValue;
        try
        {
            await RequireGrevDadAccountService().UnlinkAsync(profile.GrevId, clearLocalIfOffline: true);
            _activeGrevDadLinks.Remove(profile.GrevId);
            StopGrevDadLinkPolling();
            _profileEditView.SetGrevDadState(GrevDadAccountSnapshot.Unlinked);
            _profileEditView.ShowGrevDadStatus("Grev.dad was unlinked from this profile. Local apps, saves, history and role were not changed.");
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex))
        {
            _profileEditView.ShowGrevDadStatus($"Could not unlink Grev.dad: {ex.Message}");
        }
    }

    private void OpenGrevDadApprovalPage(Uri uri)
    {
        OpenGrevDadWebsite(uri);
    }

    private void StartGrevDadLinkPolling(string grevId, int intervalSeconds)
    {
        _grevDadPollingGrevId = grevId;
        _grevDadLinkPollTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 2, 30));
        if (_navigation.Current == Route.ProfileEdit &&
            string.Equals(_getProfileTarget()?.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
        {
            _grevDadLinkPollTimer.Start();
        }
    }

    private void StopGrevDadLinkPolling()
    {
        _grevDadPollingGrevId = null;
        _grevDadLinkPollTimer.Stop();
    }

    // ---------------------------------------------------------------------------------------
    // Formerly MainWindow.GrevDadPrivacySettings.cs - the Edit Profile privacy panel.
    // ---------------------------------------------------------------------------------------

    private bool _grevDadPrivacySettingsUiIntegrationReady;

    private void InitializeGrevDadPrivacySettingsUiIntegration()
    {
        if (_grevDadPrivacySettingsUiIntegrationReady)
        {
            return;
        }

        _grevDadPrivacySettingsUiIntegrationReady = true;
        _profileEditView.SaveGrevDadPrivacyRequested += settings => _ = SaveGrevDadPrivacyFromProfileAsync(settings);

        _navigation.RouteChanged += route =>
        {
            if (route == Route.ProfileEdit)
            {
                _ = RefreshGrevDadPrivacyForProfileAsync();
            }
        };
        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.ProfileEdit)
            {
                _dispatcher.BeginInvoke(new Action(() => _ = RefreshGrevDadPrivacyForProfileAsync()));
            }
        };
    }

    private async Task RefreshGrevDadPrivacyForProfileAsync()
    {
        var profile = _getProfileTarget();
        if (profile is null)
        {
            _profileEditView.SetGrevDadPrivacyState(
                GrevDadPrivacySettings.SafeFallback,
                "No persistent profile is selected.");
            return;
        }

        try
        {
            var settings = await RequireGrevDadPrivacySettingsService().GetAsync(profile.GrevId);
            if (_navigation.Current == Route.ProfileEdit &&
                string.Equals(_getProfileTarget()?.GrevId, profile.GrevId, StringComparison.OrdinalIgnoreCase))
            {
                _profileEditView.SetGrevDadPrivacyState(settings);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _profileEditView.SetGrevDadPrivacyState(
                GrevDadPrivacySettings.SafeFallback,
                $"Sharing is disabled locally until this profile's privacy settings can be read safely: {ex.Message}");
        }
    }

    private async Task SaveGrevDadPrivacyFromProfileAsync(GrevDadPrivacySettings settings)
    {
        var profile = _getProfileTarget();
        if (_navigation.Current != Route.ProfileEdit || profile is null || !CanManageGrevDadProfile(profile))
        {
            _profileEditView.ShowGrevDadStatus("This profile must be the current Primary User before its Grev.dad privacy settings can be changed.");
            return;
        }

        try
        {
            var saved = await RequireGrevDadPrivacySettingsService().SaveAsync(profile.GrevId, settings);
            _profileEditView.SetGrevDadPrivacyState(saved, "Saved locally for this GrevID profile.");
            await RefreshGrevDadPresenceForAsync(profile.GrevId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _profileEditView.ShowGrevDadStatus($"Could not save Grev.dad privacy settings: {ex.Message}");
            await RefreshGrevDadPrivacyForProfileAsync();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Formerly MainWindow.GrevDadFriends.cs - the Friends list/requests surface.
    // ---------------------------------------------------------------------------------------

    private readonly FriendsView _friendsView = new();
    private readonly FriendProfileView _friendProfileView = new();
    private bool _grevDadFriendsReady;

    private void InitializeGrevDadFriendsIntegration()
    {
        if (_grevDadFriendsReady) return;
        _grevDadFriendsReady = true;
        _dashboardView.FriendsRequested += (_, _) => OpenFriends();
        _friendsView.BackRequested += (_, _) => _navigation.GoBack();
        _friendsView.RefreshRequested += (_, _) => _ = RefreshFriendsSurfacesAsync(forceLoad: true);
        _friendsView.AddFriendCodeRequested += code => _ = AddFriendByCodeAsync(code);
        _friendsView.AcceptRequestRequested += id => _ = ResolveFriendRequestAsync(id, "accept");
        _friendsView.DeclineRequestRequested += id => _ = ResolveFriendRequestAsync(id, "decline");
        _friendsView.CancelRequestRequested += id => _ = ResolveFriendRequestAsync(id, "cancel");
        _friendsView.FriendSelected += OpenFriendProfile;
        _friendProfileView.BackRequested += (_, _) => _navigation.GoBack();
        InitializeMessages();
        _dashboardView.FriendProfileRequested += OpenFriendProfile;
        _navigation.RouteChanged += route =>
        {
            if (route == Route.Friends)
            {
                _routeHost.Content = _friendsView;
                _ = RefreshFriendsSurfacesAsync(forceLoad: true);
            }
            else if (route == Route.FriendProfile)
            {
                _routeHost.Content = _friendProfileView;
            }
        };
        RequireGrevDadAccountService().SnapshotChanged += (_, _) =>
            _dispatcher.BeginInvoke(new Action(() => _ = RefreshFriendsSurfacesAsync(forceLoad: false)));
        _session.Changed += (_, _) => _dispatcher.BeginInvoke(new Action(() => _ = RefreshFriendsSurfacesAsync(forceLoad: true)));
        _ = RefreshFriendsSurfacesAsync(forceLoad: true);
    }

    /// <summary>Opens the read-only detail screen for one friend. Bound to FriendsView.FriendSelected.</summary>
    private void OpenFriendProfile(GrevDadFriend friend)
    {
        _selectedMessageFriend = friend;
        _friendProfileView.SetFriend(friend, friend.UserId == _session.PrimaryUser?.GrevId);
        _friendProfileView.SetActivity(Array.Empty<GrevDadActivityEvent>());
        _navigation.Navigate(Route.FriendProfile);
        _ = LoadFriendActivityAsync(friend);
    }

    private async Task LoadFriendActivityAsync(GrevDadFriend friend)
    {
        var grevId = _session.PrimaryUser?.GrevId;
        if (grevId is null || _grevDadAccounts is null)
        {
            return;
        }

        try
        {
            var events = await _grevDadAccounts.GetActivityAsync(grevId, limit: 30, allowCachedWhenOffline: true);
            var filtered = events
                .Where(activity => string.Equals(activity.User.UserId, friend.UserId, StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToArray();

            if (_navigation.Current == Route.FriendProfile && grevId == _session.PrimaryUser?.GrevId
                && _selectedMessageFriend?.UserId == friend.UserId)
            {
                _friendProfileView.SetActivity(filtered);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException)
        {
            // Best-effort: the friend profile screen still shows their card/level/status without it.
        }
    }

    /// <summary>Navigates to the Friends route if this GrevID is currently linked/offline-cached. Bound to the header's Friends button.</summary>
    public void OpenFriends()
    {
        var grevId = _session.PrimaryUser?.GrevId;
        if (grevId is null) return;
        var state = RequireGrevDadAccountService().GetLastSnapshot(grevId).State;
        if (state is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline)
            _navigation.Navigate(Route.Friends);
    }

    private async Task RefreshFriendsSurfacesAsync(bool forceLoad)
    {
        var service = _grevDadAccounts;
        var primary = _session.PrimaryUser;
        if (service is null || primary?.GrevId is null)
        {
            SetFriendsUnavailable();
            return;
        }

        try
        {
            var snapshot = forceLoad ? await service.LoadLocalStateAsync(primary.GrevId) : service.GetLastSnapshot(primary.GrevId);
            var available = snapshot.State is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline;
            if (!available) { SetFriendsUnavailable(); return; }
            var friends = await service.GetFriendsAsync(primary.GrevId, allowCachedWhenOffline: true);
            var offline = snapshot.State == GrevDadConnectionState.Offline;
            var requests = offline ? GrevDadFriendRequestsSnapshot.Empty : await service.GetFriendRequestsAsync(primary.GrevId);
            var self = await BuildSelfPreviewCardAsync(primary);
            if (primary.GrevId != _session.PrimaryUser?.GrevId) return;
            _shellFriendsButton.Visibility = Visibility.Visible;
            _dashboardView.SetFriends(true, friends, offline);
            _friendsView.SetFriends(snapshot.Account?.DisplayName ?? primary.DisplayName, snapshot.Account?.FriendCode, friends, requests, offline, self);
            if (!offline)
            {
                try
                {
                    var inbox = await service.GetInboxAsync(primary.GrevId);
                    if (primary.GrevId == _session.PrimaryUser?.GrevId)
                        _friendsView.SetUnreadMessages(inbox.Conversations ?? Array.Empty<GrevDadConversation>());
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException) { }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (primary.GrevId != _session.PrimaryUser?.GrevId) return;
            var snapshot = service.GetLastSnapshot(primary.GrevId);
            var available = snapshot.State is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline;
            _shellFriendsButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            _dashboardView.SetFriends(available, Array.Empty<GrevDadFriend>(), offline: true);
            if (_navigation.Current == Route.Friends) _friendsView.ShowStatus($"Friends could not be refreshed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds a GrevDadFriend-shaped preview of the signed-in profile's own card so it can be
    /// rendered through the exact same card template real friends use - the point is to let you
    /// see how your card actually looks before adding anyone or making more profiles. Styling
    /// (theme/frame/avatar shape/visible fields) comes from the live local presentation settings;
    /// Level/XP are computed the same way GrevDadProfileSyncService computes them for a real sync,
    /// so the preview shows real numbers rather than a fabricated placeholder.
    /// </summary>
    private async Task<GrevDadFriend?> BuildSelfPreviewCardAsync(SessionUser primary)
    {
        var grevId = primary.GrevId;
        if (grevId is null)
        {
            return null;
        }

        try
        {
            var presentation = await new ProfilePresentationSettingsService(_paths).GetAsync(grevId);
            var stats = await new ProfileStatsService(
                [new GrevHomeProfileStatsSource(new PlaytimeService(_paths))], _paths)
                .GetAsync(grevId, []);
            var xp = stats.Progression.TotalXp;
            var level = stats.Progression.Level;
            var localProfile = (await _profileService.GetProfilesAsync())
                .FirstOrDefault(item => string.Equals(item.GrevId, grevId, StringComparison.OrdinalIgnoreCase));

            var card = new GrevDadPublicCard(
                Theme: ProfileBannerCatalog.Normalize(presentation.BannerKey),
                Frame: presentation.CardFrame.ToString().ToLowerInvariant(),
                AvatarShape: presentation.AvatarShape.ToString().ToLowerInvariant(),
                ShowUsername: presentation.ShowUsername,
                ShowLevel: presentation.ShowLevel,
                ShowXp: presentation.ShowXp,
                ShowPlaytime: presentation.ShowPlaytime,
                ShowSessions: presentation.ShowSessions,
                ShowStatus: presentation.ShowStatus,
                AvatarMedia: ProfileMediaDataUrl.TryRead(_paths, grevId, localProfile?.AvatarImageFile),
                CoverMedia: ProfileMediaDataUrl.TryRead(_paths, grevId, presentation.BannerImageFile),
                Bio: localProfile?.Bio ?? "",
                StatusMessage: localProfile?.StatusMessage ?? "");

            return new GrevDadFriend(
                UserId: grevId,
                Username: primary.Username ?? grevId,
                DisplayName: primary.DisplayName,
                IsVerified: false,
                FriendsSinceUtc: DateTimeOffset.UtcNow,
                Presence: new GrevDadPresence("online", "", "none", "Preview of your card", null, null),
                PublicCard: card,
                TotalXp: xp,
                Level: level);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The preview card is a convenience, not account data. If local settings/playtime
            // can't be read safely, just skip the preview rather than affecting real friends.
            return null;
        }
    }

    private async Task AddFriendByCodeAsync(string code)
    {
        var grevId = _session.PrimaryUser?.GrevId;
        if (grevId is null || _grevDadAccounts is null) return;
        try
        {
            _friendsView.ShowStatus("Looking up friend code…");
            var member = await _grevDadAccounts.FindByFriendCodeAsync(grevId, code);
            await _grevDadAccounts.SendFriendRequestAsync(grevId, member.UserId);
            _friendsView.ShowStatus($"Friend request sent to {member.DisplayName} (@{member.Username}).");
            await RefreshFriendsSurfacesAsync(forceLoad: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException)
        { _friendsView.ShowStatus(ex.Message); }
    }

    private async Task ResolveFriendRequestAsync(string requestId, string action)
    {
        var grevId = _session.PrimaryUser?.GrevId;
        if (grevId is null || _grevDadAccounts is null) return;
        try
        {
            if (action == "accept") await _grevDadAccounts.AcceptFriendRequestAsync(grevId, requestId);
            else if (action == "decline") await _grevDadAccounts.DeclineFriendRequestAsync(grevId, requestId);
            else await _grevDadAccounts.CancelFriendRequestAsync(grevId, requestId);
            await RefreshFriendsSurfacesAsync(forceLoad: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        { _friendsView.ShowStatus(ex.Message); }
    }

    private void SetFriendsUnavailable()
    {
        _shellFriendsButton.Visibility = Visibility.Collapsed;
        _dashboardView.SetFriends(false, Array.Empty<GrevDadFriend>(), offline: false);
        if (_navigation.Current == Route.Friends) _navigation.GoBack();
    }

    // ---------------------------------------------------------------------------------------
    // Formerly MainWindow.GrevDadWeb.cs - the in-shell Grev.dad/general web browser overlay.
    // ---------------------------------------------------------------------------------------

    private readonly GrevDadWebView _grevDadWebView = new();
    private Uri? _grevDadWebTarget;
    private string? _grevDadWebOwner;
    private string? _grevDadWebRequestedOwner;
    private bool _grevDadWebRequiresActiveOwner = true;
    private bool _generalWebBrowser;

    private void InitializeGrevDadWebIntegration()
    {
        _dashboardView.GrevDadRequested += (_, _) => OpenGrevDadWebsite(RequireGrevDadAccountService().BaseUri);
        _dashboardView.WebBrowserRequested += (_, _) =>
        {
            if (_session.PrimaryUser?.GrevId is null) { _dashboardView.ShowStatus("Choose a local profile to open the browser."); return; }
            _generalWebBrowser = true; _grevDadWebTarget = new Uri("https://www.google.com/"); _grevDadWebRequestedOwner = null; _grevDadWebRequiresActiveOwner = true; _navigation.Navigate(Route.GrevDadWeb);
        };
        _grevDadWebView.ExitRequested += (_, _) => _navigation.GoBack();
        _navigation.RouteChanged += route =>
        {
            if (route == Route.GrevDadWeb)
            {
                _routeHost.Content = _grevDadWebView;
                var grevId = _grevDadWebRequestedOwner ?? _session.PrimaryUser?.GrevId;
                if (grevId is null) { _navigation.GoBack(); return; }
                _grevDadWebOwner = grevId;
                var home = _generalWebBrowser ? new Uri("https://www.google.com/") : RequireGrevDadAccountService().BaseUri;
                var folder = _generalWebBrowser ? "WebBrowser" : "GrevDad";
                _ = _grevDadWebView.OpenAsync(Path.Combine(_paths.GetProfileConnections(grevId), folder, "Browser"), home, _grevDadWebTarget ?? home, _generalWebBrowser);
            }
            else
            {
                _grevDadWebView.Dispose(); _grevDadWebOwner = null; _grevDadWebRequestedOwner = null;
            }
        };
        _session.Changed += (_, _) => _dispatcher.BeginInvoke(new Action(() =>
        {
            if (_navigation.Current == Route.GrevDadWeb && _grevDadWebRequiresActiveOwner &&
                !string.Equals(_grevDadWebOwner, _session.PrimaryUser?.GrevId, StringComparison.OrdinalIgnoreCase))
            { _grevDadWebView.Dispose(); _navigation.GoBack(); }
        }));
    }

    private void OpenGrevDadWebsite(Uri target)
    {
        if (_session.PrimaryUser?.GrevId is null) { _dashboardView.ShowStatus("Choose a local profile before opening Grev.dad."); return; }
        var home = RequireGrevDadAccountService().BaseUri;
        if (target.Scheme != "https" || !string.Equals(target.Host, home.Host, StringComparison.OrdinalIgnoreCase) || target.Port != home.Port)
        { _profileEditView.ShowGrevDadStatus("The approval address is not on the configured Grev.dad website."); return; }
        _grevDadWebTarget = target;
        _grevDadWebRequestedOwner = null;
        _grevDadWebRequiresActiveOwner = true;
        _generalWebBrowser = false;
        _navigation.Navigate(Route.GrevDadWeb);
    }

    private void OpenGrevDadWebsite(LocalProfile owner, Uri target)
    {
        var home = RequireGrevDadAccountService().BaseUri;
        if (target.Scheme != "https" || !string.Equals(target.Host, home.Host, StringComparison.OrdinalIgnoreCase) || target.Port != home.Port)
        { _createProfileView.ShowGrevDadOnboardingStatus("The requested page is not on the configured Grev.dad website."); return; }
        _grevDadWebTarget = target;
        _grevDadWebRequestedOwner = owner.GrevId;
        _grevDadWebRequiresActiveOwner = false;
        _generalWebBrowser = false;
        _navigation.Navigate(Route.GrevDadWeb);
    }
}
