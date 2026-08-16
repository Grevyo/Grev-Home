using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GrevHome.Machine;
using GrevHome.Storage;

namespace GrevHome;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\GrevHome.Shell.Instance";
    private const string ActivationEventName = @"Local\GrevHome.Shell.Activate";
    private const string RecoveryArgument = "--recover-after";
    private const string CrashRestartCountVariable = "GREV_HOME_CRASH_RESTART_COUNT";
    private const int MaximumAutomaticCrashRestarts = 2;

    private readonly AppPaths _paths = new();
    private readonly StartupRegistrationService _startupRegistration = new();
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationListenerCancellation;
    private bool _ownsInstanceMutex;
    private bool _fatalCrashObserved;
    private int _crashRestartRequested;
    private string? _shellMarkerPath;

    public App()
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleAppDomainUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        WaitForRecoveringProcessIfRequested(e.Args);

        _singleInstanceMutex = new Mutex(initiallyOwned: false, InstanceMutexName);
        try
        {
            _ownsInstanceMutex = _singleInstanceMutex.WaitOne(0, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            _ownsInstanceMutex = true;
        }

        if (!_ownsInstanceMutex)
        {
            SignalExistingInstance();
            Shutdown(0);
            return;
        }

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _activationListenerCancellation = new CancellationTokenSource();

        _paths.EnsureMachineLayout();
        RecordShellStart();

        if (!_startupRegistration.TryEnsureRegistered(out var startupFailure) &&
            _startupRegistration.IsApplianceExecutable &&
            !string.IsNullOrWhiteSpace(startupFailure))
        {
            WriteLifecycleLog($"Windows autostart registration failed: {startupFailure}");
        }

        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        StartActivationListener();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_fatalCrashObserved)
        {
            ClearShellMarker();
        }

        _activationListenerCancellation?.Cancel();
        try
        {
            _activationEvent?.Set();
        }
        catch (ObjectDisposedException)
        {
        }

        _activationEvent?.Dispose();
        _activationListenerCancellation?.Dispose();

        if (_ownsInstanceMutex)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void StartActivationListener()
    {
        var activationEvent = _activationEvent;
        var cancellation = _activationListenerCancellation;
        if (activationEvent is null || cancellation is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (!activationEvent.WaitOne(TimeSpan.FromMilliseconds(500)))
                {
                    continue;
                }

                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                Dispatcher.BeginInvoke(new Action(ActivateExistingShell));
            }
        });
    }

    private void ActivateExistingShell()
    {
        if (MainWindow is not { } window)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Maximized;
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The existing process is still in its earliest startup window. A second Grev Home
            // process still exits rather than creating a second shell/runtime owner.
        }
    }

    private static void WaitForRecoveringProcessIfRequested(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (!string.Equals(args[index], RecoveryArgument, StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(args[index + 1], out var processId) || processId <= 0)
            {
                continue;
            }

            try
            {
                using var previous = Process.GetProcessById(processId);
                previous.WaitForExit(15000);
            }
            catch (ArgumentException)
            {
                // The crashed process has already exited.
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }
    }

    private void RecordShellStart()
    {
        _shellMarkerPath = Path.Combine(_paths.RuntimeData, "shell-session.marker");
        if (File.Exists(_shellMarkerPath))
        {
            WriteLifecycleLog("Previous Grev Home shell session did not record a clean exit. Runtime recovery will validate persisted app sessions before use.");
        }

        try
        {
            File.WriteAllText(
                _shellMarkerPath,
                $"pid={Environment.ProcessId}{Environment.NewLine}started={DateTimeOffset.Now:O}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteLifecycleLog($"Could not write shell session marker: {ex.Message}");
        }
    }

    private void ClearShellMarker()
    {
        if (string.IsNullOrWhiteSpace(_shellMarkerPath))
        {
            return;
        }

        try
        {
            File.Delete(_shellMarkerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteLifecycleLog($"Could not clear shell session marker: {ex.Message}");
        }
    }

    private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _fatalCrashObserved = true;
        WriteCrashLog("WPF Dispatcher", e.Exception);
        TryScheduleCrashRestart();
    }

    private void HandleAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        _fatalCrashObserved = true;
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog("AppDomain", exception);
        }
        else
        {
            WriteCrashLog("AppDomain", new InvalidOperationException($"Unhandled non-Exception object: {e.ExceptionObject}"));
        }

        TryScheduleCrashRestart();
    }

    private void TryScheduleCrashRestart()
    {
        if (Interlocked.Exchange(ref _crashRestartRequested, 1) != 0 ||
            !_startupRegistration.IsApplianceExecutable)
        {
            return;
        }

        var currentCount = int.TryParse(
            Environment.GetEnvironmentVariable(CrashRestartCountVariable),
            out var parsed)
            ? parsed
            : 0;
        if (currentCount >= MaximumAutomaticCrashRestarts)
        {
            WriteLifecycleLog("Automatic crash restart suppressed to prevent a restart loop.");
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(RecoveryArgument);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.Environment[CrashRestartCountVariable] = (currentCount + 1).ToString();
            Process.Start(startInfo);
            WriteLifecycleLog($"Scheduled automatic shell recovery attempt {currentCount + 1} of {MaximumAutomaticCrashRestarts}.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            WriteLifecycleLog($"Could not schedule automatic shell recovery: {ex.Message}");
        }
    }

    private void WriteCrashLog(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(_paths.Logs);
            var logPath = Path.Combine(_paths.Logs, "grevhome-crash.log");
            var entry = $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(logPath, entry);
        }
        catch
        {
            // Crash logging must never replace the original failure with a logging failure.
        }
    }

    private void WriteLifecycleLog(string message)
    {
        try
        {
            Directory.CreateDirectory(_paths.Logs);
            var logPath = Path.Combine(_paths.Logs, "grevhome-shell.log");
            File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
