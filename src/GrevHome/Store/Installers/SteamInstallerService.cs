using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Win32;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Store.Installers;

/// <summary>
/// Steam-specific Global App installer. Steam owns its account state, game libraries and native
/// self-update lifecycle. Grev Home owns only trusted installation/registration, per-GrevID
/// library membership, launch/runtime behavior and Grev controller/presentation settings.
/// </summary>
public sealed class SteamInstallerService : ITrustedPackageInstaller
{
    public const string InstallerId = "steam";

    private static readonly Uri OfficialWindowsInstaller = new(
        "https://cdn.fastly.steamstatic.com/client/installer/SteamSetup.exe");

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly AppPaths _paths;
    private readonly InstalledAppService _installedApps;

    public SteamInstallerService(AppPaths paths, InstalledAppService installedApps)
    {
        _paths = paths;
        _installedApps = installedApps;
    }

    string ITrustedPackageInstaller.InstallerId => InstallerId;

    public async Task<PackageHealthSnapshot> InspectAsync(
        PackageOperationContext context,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(context.Package);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryFindSteamInstallation(out var installation))
        {
            return new PackageHealthSnapshot(
                PackageHealthState.RepairRecommended,
                "Steam is registered in Grev Home but steam.exe could not be found on this Windows machine.");
        }

        var registered = (await _installedApps.GetMachineInstalledAsync(cancellationToken))
            .FirstOrDefault(entry => string.Equals(
                entry.Manifest.Definition.AppId,
                context.Package.App.AppId,
                StringComparison.OrdinalIgnoreCase));

        if (registered is not null)
        {
            var registeredExecutable = Environment.ExpandEnvironmentVariables(
                registered.Manifest.Definition.Launch.Executable);
            if (!PathsEqual(registeredExecutable, installation.Executable))
            {
                return new PackageHealthSnapshot(
                    PackageHealthState.RepairRecommended,
                    $"Steam was found at {installation.InstallRoot}, but Grev Home's registered launch path is stale. Repair will refresh registration without touching Steam games or account data.",
                    installation.Version);
            }
        }

        return new PackageHealthSnapshot(
            PackageHealthState.Healthy,
            $"Steam is installed at {installation.InstallRoot}. Steam owns its account, game-library and client-update data.",
            installation.Version);
    }

    Task ITrustedPackageInstaller.InstallAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        InstallAsync(context.Package, progress, cancellationToken);

    Task ITrustedPackageInstaller.UpdateAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidatePackage(context.Package);
        throw new InvalidOperationException(
            "Steam owns its client update lifecycle. Grev Home does not replace Steam's native updater with a second update mechanism.");
    }

    async Task ITrustedPackageInstaller.RepairAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidatePackage(context.Package);
        cancellationToken.ThrowIfCancellationRequested();

        if (TryFindSteamInstallation(out var installation))
        {
            progress?.Report(new PackageInstallProgress(
                "Repair",
                "Steam is present. Refreshing the detected Steam launch path and Grev Home registration without changing games or account data…",
                82));
            await RegisterAsync(context.Package, installation, cancellationToken);
            progress?.Report(new PackageInstallProgress(
                "Complete",
                $"Steam {installation.Version} is registered from {installation.InstallRoot}.",
                100));
            return;
        }

        progress?.Report(new PackageInstallProgress(
            "Repair",
            "Steam files were not detected. Re-running the trusted Steam installation workflow…",
            5));
        await InstallAsync(context.Package, progress, cancellationToken);
    }

    Task ITrustedPackageInstaller.UninstallAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidatePackage(context.Package);
        throw new InvalidOperationException(
            "Steam machine uninstall is intentionally disabled in Grev Home for now because removing Steam can affect installed game content. Remove Steam from individual GrevID libraries instead.");
    }

    public async Task InstallAsync(
        GrevStorePackageDefinition package,
        IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(package);
        _paths.EnsureMachineLayout();

        if (TryFindSteamInstallation(out var existing))
        {
            progress?.Report(new PackageInstallProgress(
                "Register",
                $"Steam is already installed at {existing.InstallRoot}. Registering that installation with Grev Home…",
                88));
            await RegisterAsync(package, existing, cancellationToken);
            progress?.Report(new PackageInstallProgress(
                "Complete",
                $"Steam {existing.Version} is ready in Grev Home. No reinstall was needed.",
                100));
            return;
        }

        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "GrevHome",
            "Installers",
            "steam",
            Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(stagingRoot, "SteamSetup.exe");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            progress?.Report(new PackageInstallProgress(
                "Download",
                "Downloading Valve's current SteamSetup.exe from the official Steam CDN…",
                0));
            await DownloadInstallerAsync(installerPath, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PackageInstallProgress(
                "Install",
                "Running Steam's Windows bootstrap installer silently. Windows may ask for administrator approval…",
                72));
            await RunSilentInstallerAsync(installerPath, cancellationToken);

            progress?.Report(new PackageInstallProgress(
                "Verify",
                "Waiting for the Steam installation and steam.exe to become available…",
                88));
            var installation = await WaitForSteamInstallationAsync(cancellationToken);

            progress?.Report(new PackageInstallProgress(
                "Register",
                "Registering the detected Steam installation and Big Picture launch contract with Grev Home…",
                96));
            await RegisterAsync(package, installation, cancellationToken);
            progress?.Report(new PackageInstallProgress(
                "Complete",
                $"Steam {installation.Version} is installed at {installation.InstallRoot} and ready for Big Picture launch.",
                100));
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private async Task RegisterAsync(
        GrevStorePackageDefinition package,
        SteamInstallation installation,
        CancellationToken cancellationToken)
    {
        // Persist the path Windows/Steam actually uses. Existing Steam installs can live outside
        // Program Files, and Grev Home must not replace that with a guessed default path.
        var installedDefinition = package.App with
        {
            Launch = package.App.Launch with
            {
                Executable = installation.Executable,
                WorkingDirectory = installation.InstallRoot
            }
        };

        await _installedApps.RegisterInstalledAsync(
            installedDefinition,
            string.IsNullOrWhiteSpace(installation.Version) ? "current" : installation.Version,
            ownerGrevId: null,
            cancellationToken);
    }

    private static async Task DownloadInstallerAsync(
        string destination,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(
            OfficialWindowsInstaller,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var length = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destination);
        var buffer = new byte[128 * 1024];
        long copied = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;

            if (length is > 0)
            {
                var downloadPercent = Math.Clamp(copied * 68d / length.Value, 0d, 68d);
                progress?.Report(new PackageInstallProgress(
                    "Download",
                    $"Downloading Steam… {copied / 1024d / 1024d:0.0} MB / {length.Value / 1024d / 1024d:0.0} MB",
                    downloadPercent));
            }
        }

        if (new FileInfo(destination).Length < 1024 * 1024)
        {
            throw new InvalidDataException("Valve's Steam download did not return a plausible Windows installer.");
        }
    }

    private static async Task RunSilentInstallerAsync(
        string installerPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/S",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Environment.CurrentDirectory
            }) ?? throw new InvalidOperationException("Windows did not start SteamSetup.exe.");

            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 && !TryFindSteamInstallation(out _))
            {
                throw new InvalidOperationException(
                    $"SteamSetup.exe exited with code {process.ExitCode} and steam.exe was not detected. Grev Home did not register a broken installation.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Steam installation was cancelled at the Windows administrator prompt.", ex);
        }
    }

    private static async Task<SteamInstallation> WaitForSteamInstallationAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 180; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryFindSteamInstallation(out var installation))
            {
                return installation;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new InvalidOperationException(
            "SteamSetup.exe returned, but Grev Home could not find steam.exe through Steam's registry paths or the standard Windows Steam locations. Nothing was registered as installed.");
    }

    private static bool TryFindSteamInstallation(out SteamInstallation installation)
    {
        var candidates = new List<string>();

        AddRegistryCandidates(candidates, RegistryHive.CurrentUser, RegistryView.Default);
        AddRegistryCandidates(candidates, RegistryHive.LocalMachine, RegistryView.Registry32);
        AddRegistryCandidates(candidates, RegistryHive.LocalMachine, RegistryView.Registry64);

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "Steam", "steam.exe"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Steam", "steam.exe"));
        }

        foreach (var candidate in candidates
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(NormalizeCandidate)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate)) continue;

            var installRoot = Path.GetDirectoryName(candidate);
            if (string.IsNullOrWhiteSpace(installRoot)) continue;

            var versionInfo = FileVersionInfo.GetVersionInfo(candidate);
            var version = versionInfo.ProductVersion
                          ?? versionInfo.FileVersion
                          ?? "current";
            if (version.Length > 40)
            {
                version = version[..40];
            }

            installation = new SteamInstallation(candidate, installRoot, version);
            return true;
        }

        installation = default!;
        return false;
    }

    private static void AddRegistryCandidates(
        ICollection<string> candidates,
        RegistryHive hive,
        RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key is null) return;

            if (key.GetValue("SteamExe") is string steamExe && !string.IsNullOrWhiteSpace(steamExe))
            {
                candidates.Add(steamExe);
            }

            var path = key.GetValue("SteamPath") as string
                       ?? key.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                candidates.Add(Path.Combine(path, "steam.exe"));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Registry view is not readable for this process; other views/common paths remain.
        }
        catch (System.Security.SecurityException)
        {
            // Registry view is not readable for this process; other views/common paths remain.
        }
    }

    private static string NormalizeCandidate(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))
            .Replace('/', Path.DirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return expanded;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left.Trim().Trim('"')),
                Path.GetFullPath(right.Trim().Trim('"')),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void ValidatePackage(GrevStorePackageDefinition package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!string.Equals(package.InstallerId, InstallerId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.App.AppId, "steam", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Steam installer can only manage the trusted Steam package.");
        }

        if (package.App.InstallStrategy != InstallStrategy.SystemInstalled ||
            package.App.DataStrategy != DataStrategy.NativeAccount)
        {
            throw new InvalidOperationException(
                "Steam must remain a Global Windows installation with Steam-owned account and game-library data.");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GrevHome/0.14 SteamInstaller");
        return client;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record SteamInstallation(
        string Executable,
        string InstallRoot,
        string Version);
}
