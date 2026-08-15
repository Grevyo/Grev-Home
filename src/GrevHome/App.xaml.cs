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

    private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("WPF Dispatcher", e.Exception);

        // Controller input raises WPF button/focus actions programmatically. A failure in that
        // narrow interaction path must not terminate the entire console shell. Keep the shell
        // alive, log the full stack, and let the next input action continue normally.
        if (IsControllerInteractionFailure(e.Exception))
        {
            e.Handled = true;

            if (MainWindow is { } mainWindow)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    if (!mainWindow.IsVisible)
                    {
                        mainWindow.Show();
                    }

                    mainWindow.Activate();
                    mainWindow.Focus();
                }));
            }
        }
    }

    private static void HandleAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog("AppDomain", exception);
            return;
        }

        WriteCrashLog("AppDomain", new InvalidOperationException($"Unhandled non-Exception object: {e.ExceptionObject}"));
    }

    private static bool IsControllerInteractionFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var stack = current.StackTrace ?? string.Empty;
            if (stack.Contains("GrevHome.MainWindow.HandleInput", StringComparison.Ordinal) ||
                stack.Contains("GrevHome.MainWindow.ActivateFocusedControl", StringComparison.Ordinal) ||
                stack.Contains("GrevHome.MainWindow.MoveFocus", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
