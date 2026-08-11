using System.IO;
using System.Windows;
using System.Windows.Threading;
using GrevHome.Apps;
using GrevHome.Input;
using GrevHome.Navigation;
using GrevHome.Store;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly AppSettingsView _appSettingsView = new();
    private AppControllerProfileService? _appControllerProfileService;
    private AppPresentationService? _appPresentationService;
    private InstalledAppEntry? _appSettingsEntry;
    private bool _appSettingsIntegrationReady;
    private bool _installedLibraryActionMenuBackEntryArmed;
    private bool _openingInstalledLibraryActionMenuBackEntry;

    private void InitializeAppSettingsIntegration()
    {
        if (_appSettingsIntegrationReady) return;
        _appSettingsIntegrationReady = true;

        _appControllerProfileService = new AppControllerProfileService(_paths);
        _appPresentationService = new AppPresentationService(_paths);

        _installedLibraryView.SettingsRequested += entry =>
        {
            CloseInstalledLibraryActionMenuForAction();
            OpenAppSettings(entry);
        };
        _installedLibraryView.ActionMenuLaunchRequested += entry =>
        {
            CloseInstalledLibraryActionMenuForAction();
            _ = LaunchInstalledAppAsync(entry);
        };
        _installedLibraryView.StoreRequested += OpenInstalledAppStorePage;
        _installedLibraryView.SwitchRequested += launchSessionId =>
        {
            CloseInstalledLibraryActionMenuForAction();
            SwitchToSession(launchSessionId);
        };
        _installedLibraryView.RestartRequested += launchSessionId =>
        {
            CloseInstalledLibraryActionMenuForAction();
            _ = RestartRuntimeSessionAsync(launchSessionId);
        };
        _installedLibraryView.CloseRequested += launchSessionId =>
        {
            CloseInstalledLibraryActionMenuForAction();
            RequestCloseSession(launchSessionId);
            _installedLibraryView.ShowStatus("Close requested. Grev Home will keep tracking the app until its processes exit.");
        };
        _installedLibraryView.ForceKillRequested += launchSessionId =>
        {
            CloseInstalledLibraryActionMenuForAction();
            ForceCloseSession(launchSessionId);
            _installedLibraryView.ShowStatus("Force Kill requested for the selected app's tracked process tree.");
        };
        _installedLibraryView.AppKillerRequested += entry =>
        {
            CloseInstalledLibraryActionMenuForAction();
            OpenAppKiller(entry.Manifest.Definition.AppId);
        };
        _installedLibraryView.RunningAppsRequested += (_, _) =>
        {
            CloseInstalledLibraryActionMenuForAction();
            OpenRunningApps();
        };
        _installedLibraryView.ActionMenuOpened += (_, _) => OpenInstalledLibraryActionMenuBackEntry();
        _installedLibraryView.ActionMenuCancelRequested += (_, _) => CancelInstalledLibraryActionMenu();

        _grevStoreAppView.SettingsRequested += OpenAppSettings;
        _appSettingsView.SaveRequested += draft => _ = SaveAppControllerProfileAsync(draft);
        _appSettingsView.ResetRequested += (_, _) => _ = ResetAppControllerProfileAsync();
        _appSettingsView.ResetOnboardingRequested += (_, _) => ResetAppOnboarding();
        _appSettingsView.ResetPresentationRequested += (_, _) => _ = ResetAppPresentationAsync();
        _appSettingsView.BackRequested += (_, _) => _navigation.GoBack();
        _navigation.RouteChanged += HandleAppSettingsRouteChanged;

        _controllerInput.ActionPressed += HandleInstalledLibraryControllerPress;
        _controllerInput.AcceptLongPressed += controllerIndex =>
            Dispatcher.BeginInvoke(new Action(() => _installedLibraryView.HandleControllerAppLongPress(controllerIndex)));
        _controllerInput.AcceptReleased += controllerIndex =>
            Dispatcher.BeginInvoke(new Action(() => _installedLibraryView.CompleteControllerAppPress(controllerIndex)));
        _controllerInput.AcceptCancelled += controllerIndex =>
            Dispatcher.BeginInvoke(new Action(() => _installedLibraryView.CancelControllerAppPress(controllerIndex)));

        _runtimeSessions.SessionChanged += _ =>
            Dispatcher.BeginInvoke(new Action(RefreshInstalledLibraryRuntimeState));
        _runtimeSessions.SessionEnded += _ =>
            Dispatcher.BeginInvoke(new Action(RefreshInstalledLibraryRuntimeState));

        _session.Changed += (_, _) =>
        {
            if (_navigation.Current == Route.AppSettings)
            {
                Dispatcher.BeginInvoke(new Action(() => _ = RefreshAppSettingsAsync()));
            }
        };
    }

    private void HandleInstalledLibraryControllerPress(ControllerInputEventArgs input)
    {
        if (input.Action != InputAction.Accept ||
            _navigation.Current != Route.InstalledLibrary ||
            _installedLibraryView.IsActionMenuOpen ||
            IsStoreModalOpen ||
            IsPowerMenuOpen ||
            _overlayWindow.IsOpen)
        {
            return;
        }

        // MainWindow's normal Accept handler is queued with Dispatcher.BeginInvoke. Use the
        // higher-priority synchronous dispatcher call here so the app tile can temporarily
        // suppress that queued click until A is released or reaches the long-press threshold.
        Dispatcher.Invoke(() =>
        {
            if (_navigation.Current == Route.InstalledLibrary && !_installedLibraryView.IsActionMenuOpen)
            {
                _installedLibraryView.BeginControllerAppPress(input.ControllerIndex);
            }
        });
    }

    private void OpenInstalledLibraryActionMenuBackEntry()
    {
        RefreshInstalledLibraryRuntimeState();
        if (_navigation.Current != Route.InstalledLibrary || _installedLibraryActionMenuBackEntryArmed)
        {
            return;
        }

        _installedLibraryActionMenuBackEntryArmed = true;
        _openingInstalledLibraryActionMenuBackEntry = true;
        _navigation.Navigate(Route.InstalledLibrary, allowSameRoute: true);
    }

    private void CancelInstalledLibraryActionMenu()
    {
        if (_installedLibraryActionMenuBackEntryArmed)
        {
            _navigation.GoBack();
            return;
        }

        _installedLibraryView.CloseActionMenu();
    }

    private void CloseInstalledLibraryActionMenuForAction()
    {
        if (_installedLibraryActionMenuBackEntryArmed)
        {
            _navigation.DiscardBackEntry(Route.InstalledLibrary);
        }

        _installedLibraryActionMenuBackEntryArmed = false;
        _openingInstalledLibraryActionMenuBackEntry = false;
        _installedLibraryView.CloseActionMenu();
    }

    private void OpenInstalledAppStorePage(InstalledAppEntry entry)
    {
        var package = _grevStoreCatalog.Find(entry.Manifest.Definition.AppId);
        if (package is null)
        {
            _installedLibraryView.ShowStatus("This installed app does not have a Grev Store package page.");
            return;
        }

        CloseInstalledLibraryActionMenuForAction();
        OpenStorePackage(package);
    }

    private void OpenAppKiller(string preferredAppId)
    {
        if (!_session.HasSignedInUsers)
        {
            _navigation.Reset(Route.Login);
            return;
        }

        _appKillerView.SetPreferredApp(preferredAppId);
        UpdateRuntimeSurfaces();
        _navigation.Navigate(Route.AppKiller);
    }

    private void RefreshInstalledLibraryRuntimeState() =>
        _installedLibraryView.SetRunningSessions(_runtimeSessions.GetActiveSessions());

    private void OpenAppSettings(InstalledAppEntry entry)
    {
        if (!_session.HasSignedInUsers || !entry.AvailableToCurrentUser)
        {
            return;
        }

        _appSettingsEntry = entry;
        _navigation.Navigate(Route.AppSettings);
    }

    private void HandleAppSettingsRouteChanged(Route route)
    {
        if (route == Route.InstalledLibrary)
        {
            RefreshInstalledLibraryRuntimeState();

            if (_installedLibraryActionMenuBackEntryArmed)
            {
                if (_openingInstalledLibraryActionMenuBackEntry)
                {
                    _openingInstalledLibraryActionMenuBackEntry = false;
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.ContextIdle,
                        new Action(_installedLibraryView.RefocusActionMenu));
                    return;
                }

                _installedLibraryActionMenuBackEntryArmed = false;
                _installedLibraryView.CloseActionMenu();
                return;
            }
        }
        else if (_installedLibraryView.IsActionMenuOpen)
        {
            if (_installedLibraryActionMenuBackEntryArmed)
            {
                _navigation.DiscardBackEntry(Route.InstalledLibrary);
            }

            _installedLibraryActionMenuBackEntryArmed = false;
            _openingInstalledLibraryActionMenuBackEntry = false;
            _installedLibraryView.CloseActionMenu(returnFocus: false);
        }

        if (route != Route.AppSettings) return;
        RouteHost.Content = _appSettingsView;
        _ = RefreshAppSettingsAsync();
        FocusRouteSoon();
    }

    private async Task RefreshAppSettingsAsync()
    {
        var entry = _appSettingsEntry;
        var controllerService = _appControllerProfileService;
        var presentationService = _appPresentationService;
        if (entry is null || controllerService is null || presentationService is null ||
            _navigation.Current != Route.AppSettings)
        {
            return;
        }

        var primary = _session.PrimaryUser;
        var grevId = primary?.GrevId;
        var package = _grevStoreCatalog.Find(entry.Manifest.Definition.AppId);
        var controllerDefaults = package?.ControllerProfile ?? AppControllerProfileDefaults.Empty;

        ResolvedAppControllerProfile controllerProfile;
        ResolvedAppPresentation presentation;
        if (string.IsNullOrWhiteSpace(grevId))
        {
            controllerProfile = AppControllerProfileService.ResolveDefaults(controllerDefaults);
            presentation = ResolvePackagePresentationDefaults(entry, package);
        }
        else
        {
            controllerProfile = await controllerService.ResolveAsync(
                grevId,
                entry.Manifest.Definition.AppId,
                controllerDefaults);
            presentation = package is null
                ? ResolvePackagePresentationDefaults(entry, package)
                : await presentationService.ResolveAsync(grevId, package);
        }

        if (_navigation.Current != Route.AppSettings || _appSettingsEntry != entry) return;

        _appSettingsView.SetApp(
            entry,
            package,
            primary,
            controllerProfile,
            presentation,
            canSave: !string.IsNullOrWhiteSpace(grevId));
    }

    private static ResolvedAppPresentation ResolvePackagePresentationDefaults(
        InstalledAppEntry entry,
        GrevStorePackageDefinition? package)
    {
        var defaults = package?.Presentation;
        return new ResolvedAppPresentation(
            defaults?.DisplayName ?? entry.Manifest.Definition.Name,
            defaults?.TileColor ?? "#151923",
            defaults?.IconAsset,
            defaults?.TileMediaAsset,
            defaults?.HeroMediaAsset,
            HasUserOverrides: false);
    }

    private async Task SaveAppControllerProfileAsync(AppControllerProfileDraft draft)
    {
        var entry = _appSettingsEntry;
        var service = _appControllerProfileService;
        var grevId = _session.PrimaryUser?.GrevId;
        if (entry is null || service is null || string.IsNullOrWhiteSpace(grevId))
        {
            _appSettingsView.ShowStatus("A persistent local Primary GrevID is required to save app-specific settings.");
            return;
        }

        try
        {
            await service.SaveAsync(grevId, entry.Manifest.Definition.AppId, draft.Enabled, draft.Mappings);
            await RefreshAppSettingsAsync();
            _appSettingsView.ShowStatus("Controller profile saved for this GrevID. Other profiles keep their own app settings.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _appSettingsView.ShowStatus($"Could not save controller profile: {ex.Message}");
        }
    }

    private async Task ResetAppControllerProfileAsync()
    {
        var entry = _appSettingsEntry;
        var service = _appControllerProfileService;
        var grevId = _session.PrimaryUser?.GrevId;
        if (entry is null || service is null || string.IsNullOrWhiteSpace(grevId))
        {
            _appSettingsView.ShowStatus("A persistent local Primary GrevID is required to reset app-specific settings.");
            return;
        }

        try
        {
            await service.ResetAsync(grevId, entry.Manifest.Definition.AppId);
            await RefreshAppSettingsAsync();
            _appSettingsView.ShowStatus("Controller profile reset. Grev Home is showing this app's supplied defaults again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _appSettingsView.ShowStatus($"Could not reset controller profile: {ex.Message}");
        }
    }

    private void ResetAppOnboarding()
    {
        var entry = _appSettingsEntry;
        var grevId = _session.PrimaryUser?.GrevId;
        if (entry is null || string.IsNullOrWhiteSpace(grevId))
        {
            _appSettingsView.ShowStatus("A persistent local Primary GrevID is required to reset app onboarding.");
            return;
        }

        var package = _grevStoreCatalog.Find(entry.Manifest.Definition.AppId);
        if (package?.Onboarding is null)
        {
            _appSettingsView.ShowStatus("This app does not declare a reusable launch guide.");
            return;
        }

        try
        {
            _appControllerGuidePreferences ??= new AppControllerGuidePreferenceService(_paths);
            _appControllerGuidePreferences.ResetForProfile(grevId, entry.Manifest.Definition.AppId);
            foreach (var session in _runtimeSessions.GetActiveSessions().Where(session =>
                         string.Equals(session.AppId, entry.Manifest.Definition.AppId, StringComparison.OrdinalIgnoreCase)))
            {
                _controllerGuideShownSessions.Remove(session.LaunchSessionId);
            }
            _appSettingsView.ShowStatus("Launch guide reset for this GrevID. It will be eligible to appear the next time Grev Home hands control to this app.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _appSettingsView.ShowStatus($"Could not reset launch guide: {ex.Message}");
        }
    }

    private async Task ResetAppPresentationAsync()
    {
        var entry = _appSettingsEntry;
        var service = _appPresentationService;
        var grevId = _session.PrimaryUser?.GrevId;
        var package = entry is null ? null : _grevStoreCatalog.Find(entry.Manifest.Definition.AppId);
        if (entry is null || service is null || string.IsNullOrWhiteSpace(grevId) || package is null)
        {
            _appSettingsView.ShowStatus("A persistent local Primary GrevID and a Grev Store package are required to reset app presentation.");
            return;
        }

        if (!package.Supports(AppPackageCapability.PresentationOverrides))
        {
            _appSettingsView.ShowStatus("This package does not declare per-GrevID presentation overrides.");
            return;
        }

        try
        {
            await service.ResetAsync(grevId, entry.Manifest.Definition.AppId);
            await RefreshAppSettingsAsync();
            _appSettingsView.ShowStatus("App appearance reset. Grev Home is showing the package-supplied presentation defaults again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _appSettingsView.ShowStatus($"Could not reset app appearance: {ex.Message}");
        }
    }
}
