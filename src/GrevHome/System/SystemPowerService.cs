using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GrevHome.System;

public enum SystemPowerAction
{
    Shutdown,
    Restart,
    Sleep
}

public sealed class SystemPowerService
{
    public void Execute(SystemPowerAction action)
    {
        switch (action)
        {
            case SystemPowerAction.Shutdown:
                StartShutdownCommand("/s /t 0");
                break;
            case SystemPowerAction.Restart:
                StartShutdownCommand("/r /t 0");
                break;
            case SystemPowerAction.Sleep:
                if (!SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not accept the sleep request.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private static void StartShutdownCommand(string arguments)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe"),
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
        {
            throw new InvalidOperationException("Windows did not start the power command.");
        }

        process.Dispose();
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool forceCritical,
        [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);
}
