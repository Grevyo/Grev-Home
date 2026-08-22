using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using GrevHome.Diagnostics;

namespace GrevHome;

public partial class MainWindow
{
    private MachineHealthService? _machineHealth;
    private int _machineHealthCaptureActive;

    private void InitializeMachineHealthIntegration()
    {
        _machineHealth ??= new MachineHealthService(_paths);

        // Run the first snapshot only once the shell reaches dispatcher idle. Local sign-in,
        // controller startup and route creation must never wait for a recursive health scan.
        Dispatcher.BeginInvoke(
            new Action(() => _ = CaptureMachineHealthSafeAsync()),
            DispatcherPriority.ApplicationIdle);
    }

    private async Task CaptureMachineHealthSafeAsync()
    {
        var health = _machineHealth;
        if (health is null || Interlocked.Exchange(ref _machineHealthCaptureActive, 1) != 0)
        {
            return;
        }

        try
        {
            await health.CaptureAndPersistAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            // Health diagnostics are observational. If the snapshot itself cannot be persisted,
            // preserve that failure in the normal log when possible but never destabilize the shell.
            try
            {
                Directory.CreateDirectory(_paths.Logs);
                File.AppendAllText(
                    Path.Combine(_paths.Logs, "grevhome-health.log"),
                    $"[{DateTimeOffset.Now:O}] Machine health snapshot failed: {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
            }
        }
        finally
        {
            Interlocked.Exchange(ref _machineHealthCaptureActive, 0);
        }
    }
}
