using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GrevHome.Storage;

namespace GrevHome;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\GrevHome.Shell.Instance";
    private const string ActivationEventName = @"Local\GrevHome.Shell.Activate";

    private readonly AppPaths _paths = new();
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationListenerCancellation;
    private bool _ownsInstanceMutex;
    private bool _fatalCrashObserved;
    private string? _shellMarkerPath;

    public App()
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
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
        try
        {
            new LocalDataSchemaService(_paths)
                .EnsureCurrentAsync()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _fatalCrashObserved = true;
            WriteCrashLog("Local data schema startup gate", ex);
            throw;
        }

        WriteLifecycleLog(
            $"Shell starting. {BuildDiagnosticContext()} EffectiveRoot={_paths.Root}; GREV_HOME_ROOT={Environment.GetEnvironmentVariable("GREV_HOME_ROOT") ?? "<unset>"}");
        RecordShellStart();

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
            WriteLifecycleLog($"Shell clean exit. ExitCode={e.ApplicationExitCode}; {BuildDiagnosticContext()}");
        }
        else
        {
            WriteLifecycleLog($"Shell exit after fatal exception. ExitCode={e.ApplicationExitCode}; {BuildDiagnosticContext()}");
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
        TaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
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
                $"pid={Environment.ProcessId}{Environment.NewLine}started={DateTimeOffset.Now:O}{Environment.NewLine}root={_paths.Root}{Environment.NewLine}");
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
    }

    private void HandleAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        _fatalCrashObserved = true;
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog("AppDomain", exception);
            return;
        }

        WriteCrashLog("AppDomain", new InvalidOperationException($"Unhandled non-Exception object: {e.ExceptionObject}"));
    }

    private void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("Unobserved Task (non-fatal diagnostic)", e.Exception);
    }

    private void WriteCrashLog(string source, Exception exception)
    {
        var entry =
            $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}" +
            $"{BuildDiagnosticContext()} EffectiveRoot={_paths.Root}{Environment.NewLine}" +
            $"{exception}{Environment.NewLine}{Environment.NewLine}";
        AppendDiagnosticWithFallback("grevhome-crash.log", entry);
    }

    private void WriteLifecycleLog(string message)
    {
        AppendDiagnosticWithFallback(
            "grevhome-shell.log",
            $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
    }

    private void AppendDiagnosticWithFallback(string fileName, string entry)
    {
        foreach (var directory in GetDiagnosticDirectories())
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, fileName), entry);
                return;
            }
            catch
            {
                // Keep trying progressively more independent diagnostic locations. Logging must
                // never replace the original exception or prevent Grev Home from shutting down.
            }
        }
    }

    private IEnumerable<string> GetDiagnosticDirectories()
    {
        yield return _paths.Logs;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "GrevHome", "Logs");
        }

        yield return Path.Combine(Path.GetTempPath(), "GrevHome", "Logs");
    }

    private static string BuildDiagnosticContext()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        return $"Version={version}; PID={Environment.ProcessId}; Thread={Environment.CurrentManagedThreadId}; " +
               $"OS={RuntimeInformation.OSDescription}; Arch={RuntimeInformation.ProcessArchitecture}; CLR={Environment.Version}; CWD={Environment.CurrentDirectory};";
    }
}
