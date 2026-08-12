using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Win32;

namespace GrevHome.Store.Installers;

/// <summary>
/// Trusted machine prerequisite used by Windows packages which require the current Microsoft
/// Visual C++ v14 x64 runtime. The installer is downloaded only from Microsoft's stable permalink
/// and is invoked through the normal Windows elevation/UAC boundary.
/// </summary>
internal sealed class VisualCppRuntimePrerequisiteService
{
    private static readonly Uri DownloadUri = new("https://aka.ms/vc14/vc_redist.x64.exe");
    private static readonly HttpClient Http = CreateHttpClient();

    public bool IsInstalled(out string? version)
    {
        version = null;

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
                if (key is null)
                {
                    continue;
                }

                var installed = key.GetValue("Installed");
                if (installed is int installedDword && installedDword == 1)
                {
                    version = key.GetValue("Version") as string;
                    return RuntimeDllsPresent();
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Reading this HKLM key normally does not require elevation. If a machine policy
                // blocks it, the DLL presence check below still gives us a safe fallback signal.
            }
            catch (System.Security.SecurityException)
            {
            }
        }

        return RuntimeDllsPresent();
    }

    public async Task EnsureInstalledAsync(
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (IsInstalled(out var installedVersion))
        {
            progress?.Report(new PackageInstallProgress(
                "Prerequisite",
                string.IsNullOrWhiteSpace(installedVersion)
                    ? "Microsoft Visual C++ x64 runtime is already installed."
                    : $"Microsoft Visual C++ x64 runtime {installedVersion} is already installed.",
                4));
            return;
        }

        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "GrevHome",
            "Prerequisites",
            "vc-redist-x64",
            Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(stagingRoot, "vc_redist.x64.exe");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            progress?.Report(new PackageInstallProgress(
                "Prerequisite",
                "Downloading the Microsoft Visual C++ x64 runtime required by PCSX2…",
                1));
            await DownloadAsync(installerPath, cancellationToken);

            progress?.Report(new PackageInstallProgress(
                "Prerequisite",
                "Installing the Microsoft Visual C++ x64 runtime. Approve the Windows UAC prompt if it appears…",
                3));
            await InstallElevatedAsync(installerPath, cancellationToken);

            if (!IsInstalled(out installedVersion))
            {
                throw new InvalidOperationException(
                    "Microsoft Visual C++ x64 runtime setup completed but the required runtime could not be detected. Restart Windows if setup requested it, then use PCSX2 Repair.");
            }

            progress?.Report(new PackageInstallProgress(
                "Prerequisite",
                string.IsNullOrWhiteSpace(installedVersion)
                    ? "Microsoft Visual C++ x64 runtime is ready."
                    : $"Microsoft Visual C++ x64 runtime {installedVersion} is ready.",
                5));
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private static bool RuntimeDllsPresent()
    {
        var system32 = Environment.SystemDirectory;
        return File.Exists(Path.Combine(system32, "msvcp140.dll")) &&
               File.Exists(Path.Combine(system32, "vcruntime140.dll"));
    }

    private static async Task DownloadAsync(string destination, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !IsTrustedMicrosoftDownloadHost(finalUri.Host))
        {
            throw new InvalidDataException(
                "The Visual C++ runtime download did not resolve to a trusted Microsoft download host. Nothing was executed.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);

        if (output.Length < 1024 * 1024)
        {
            throw new InvalidDataException(
                "The Microsoft Visual C++ runtime download was unexpectedly small. Nothing was executed.");
        }
    }

    private static async Task InstallElevatedAsync(string installerPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/install /passive /norestart",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath()
        };

        Process process;
        try
        {
            process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("Windows could not start the Microsoft Visual C++ runtime installer.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException(
                "PCSX2 requires the Microsoft Visual C++ x64 runtime. The Windows UAC approval was cancelled, so Grev Home did not continue the PCSX2 operation.",
                ex);
        }

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken);
            // 0 = success; 3010 = success, restart requested. A restart is not forced by Grev Home.
            if (process.ExitCode is not 0 and not 3010)
            {
                throw new InvalidOperationException(
                    $"Microsoft Visual C++ x64 runtime setup returned exit code {process.ExitCode}. PCSX2 was not changed.");
            }
        }
    }

    private static bool IsTrustedMicrosoftDownloadHost(string host) =>
        host.Equals("aka.ms", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("download.visualstudio.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".visualstudio.microsoft.com", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GrevHome/0.13");
        return client;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
