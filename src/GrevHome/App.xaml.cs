using System.IO;
using System.Windows;
using System.Windows.Threading;
using GrevHome.Storage;

namespace GrevHome;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleAppDomainUnhandledException;
    }

    private static void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        WriteCrashLog("WPF Dispatcher", e.Exception);

    private static void HandleAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog("AppDomain", exception);
            return;
        }

        WriteCrashLog("AppDomain", new InvalidOperationException($"Unhandled non-Exception object: {e.ExceptionObject}"));
    }

    private static void WriteCrashLog(string source, Exception exception)
    {
        try
        {
            var paths = new AppPaths();
            Directory.CreateDirectory(paths.Logs);
            var logPath = Path.Combine(paths.Logs, "grevhome-crash.log");
            var entry = $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(logPath, entry);
        }
        catch
        {
            // Crash logging must never replace the original failure with a logging failure.
        }
    }
}
