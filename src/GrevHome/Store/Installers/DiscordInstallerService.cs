using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Win32;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Store.Installers;

/// <summary>
/// Discord-specific Windows-user installer workflow. Discord owns its native account/update
/// data under the signed-in Windows account; Grev Home owns only its Store registration,
/// runtime launch contract, library membership and per-GrevID Grev settings.
/// </summary>
public sealed class DiscordInstallerService : ITrustedPackageInstaller
{
    public const string InstallerId = "discord";

    private static readonly Uri StableWindowsX64Installer = new(
        "https://discord.com/api/downloads/distributions/app/installers/latest?channel=stable&platform=win&arch=x64");

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly AppPaths _paths;
    private readonly InstalledAppService _installedApps;

    public DiscordInstallerService(AppPaths paths, InstalledAppService installedApps)
    {
        _paths = paths;
        _installedApps = installedApps;
    }

    string ITrustedPackageInstaller.InstallerId => InstallerId;

    public Task<PackageHealthSnapshot> InspectAsync(
        PackageOperationContext context,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(context.Package);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetInstalledDiscord(out var version))
        {
            return Task.FromResult(new PackageHealthSnapshot(
                PackageHealthState.RepairRecommended,
                "Discord is registered in Grev Home but its Windows-user installation could not be found."));
        }

        return Task.FromResult(new PackageHealthSnapshot(
            PackageHealthState.Healthy,
            "Discord Update.exe and a current Discord.exe are present for this Windows account.",
            version));
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
            "Discord owns its Stable update lifecycle. Grev Home does not replace Discord's native updater with a second update mechanism.");
    }

    async Task ITrustedPackageInstaller.RepairAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidatePackage(context.Package);
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetInstalledDiscord(out var version))
        {
            progress?.Report(new PackageInstallProgress(
                "Repair",
                "Discord's Windows-user installation is healthy. Refreshing its Grev Home registration…",
                75));
            await RegisterAsync(context.Package, version, cancellationToken);
            progress?.Report(new PackageInstallProgress(
                "Complete",
                $"Discord {version} passed its health check and its Grev Home registration was refreshed.",
                100));
            return;
        }

        progress?.Report(new PackageInstallProgress(
            "Repair",
            "Discord files are missing. Re-running the trusted Discord installation workflow…",
            5));
        await InstallAsync(context.Package, progress, cancellationToken);
    }

    Task ITrustedPackageInstaller.UninstallAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        UninstallAsync(context.Package, progress, cancellationToken);

    public async Task InstallAsync(
        GrevStorePackageDefinition package,
        IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(package);
        _paths.EnsureMachineLayout();

        if (TryGetInstalledDiscord(out var existingVersion))
        {
            progress?.Report(new PackageInstallProgress(
                "Register",
                "Discord is already installed for this Windows account. Registering the existing install with Grev Home…",
                85));
            await RegisterAsync(package, existingVersion, cancellationToken);
            progress?.Report(new PackageInstallProgress("Complete", $"Discord {existingVersion} is ready in Grev Home.", 100));
            return;
        }

        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "GrevHome",
            "Installers",
            "discord",
            Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(stagingRoot, "DiscordSetup.exe");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            progress?.Report(new PackageInstallProgress("Download", "Downloading the current Discord Stable x64 installer from discord.com…", 0));
            await DownloadInstallerAsync(installerPath, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PackageInstallProgress("Install", "Starting Discord's official Windows installer…", 72));
            await RunInstallerAsync(installerPath, cancellationToken);

            progress?.Report(new PackageInstallProgress("Verify", "Waiting for Discord's Windows-user installation to become available…", 88));
            var version = await WaitForInstalledDiscordAsync(cancellationToken);

            progress?.Report(new PackageInstallProgress("Register", "Registering Discord with Grev Home…", 96));
            await RegisterAsync(package, version, cancellationToken);
            progress?.Report(new PackageInstallProgress("Complete", $"Discord {version} is installed and ready for Grev controller support.", 100));
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    public async Task UninstallAsync(
        GrevStorePackageDefinition package,
        IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(package);
        progress?.Report(new PackageInstallProgress("Uninstall", "Reading Discord's Windows uninstall registration…", 10));

        var uninstallCommand = ReadDiscordUninstallCommand();
        if (string.IsNullOrWhiteSpace(uninstallCommand))
        {
            throw new InvalidOperationException(
                "Discord's registered Windows uninstaller was not found. Nothing was deleted; use Windows Installed Apps if Discord needs manual removal.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new PackageInstallProgress("Uninstall", "Starting Discord's registered Windows uninstaller…", 35));
        await RunCommandLineAsync(uninstallCommand, cancellationToken);

        progress?.Report(new PackageInstallProgress("Verify", "Confirming Discord was removed from this Windows account…", 80));
        await WaitForDiscordRemovalAsync(cancellationToken);

        var grevRegistrationRoot = _paths.GetGlobalAppRoot(package.App.AppId);
        if (Directory.Exists(grevRegistrationRoot))
        {
            Directory.Delete(grevRegistrationRoot, recursive: true);
        }

        progress?.Report(new PackageInstallProgress(
            "Complete",
            "Discord was uninstalled for this Windows account and its Grev Home registration was removed. GrevID controller-profile and presentation settings were left intact.",
            100));
    }

    private async Task RegisterAsync(
        GrevStorePackageDefinition package,
        string version,
        CancellationToken cancellationToken)
    {
        await _installedApps.RegisterInstalledAsync(
            package.App,
            string.IsNullOrWhiteSpace(version) ? "current" : version,
            ownerGrevId: null,
            cancellationToken);
    }

    private static void ValidatePackage(GrevStorePackageDefinition package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!string.Equals(package.InstallerId, InstallerId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.App.AppId, "discord", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Discord installer can only manage the trusted Discord package.");
        }

        if (package.App.InstallStrategy != InstallStrategy.SystemInstalled ||
            package.App.DataStrategy != DataStrategy.NativeAccount)
        {
            throw new InvalidOperationException("Discord must remain a Windows-user system install with native Discord account data.");
        }
    }

    private static async Task DownloadInstallerAsync(
        string destination,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(
            StableWindowsX64Installer,
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
                    $"Downloading Discord Stable… {copied / 1024d / 1024d:0.0} MB / {length.Value / 1024d / 1024d:0.0} MB",
                    downloadPercent));
            }
        }

        if (new FileInfo(destination).Length < 1024 * 1024)
        {
            throw new InvalidDataException("Discord's official download did not return a plausible Windows installer.");
        }
    }

    private static async Task RunInstallerAsync(string installerPath, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Environment.CurrentDirectory
        }) ?? throw new InvalidOperationException("Windows did not start DiscordSetup.exe.");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 && !TryGetInstalledDiscord(out _))
        {
            throw new InvalidOperationException($"DiscordSetup.exe exited with code {process.ExitCode} and Discord was not detected as installed.");
        }
    }

    private static async Task<string> WaitForInstalledDiscordAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetInstalledDiscord(out var version))
            {
                return version;
            }
            await Task.Delay(500, cancellationToken);
        }

        throw new InvalidOperationException(
            "Discord's installer finished but %LocalAppData%\\Discord\\Update.exe and a current Discord.exe were not detected.");
    }

    private static async Task WaitForDiscordRemovalAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetInstalledDiscord(out _))
            {
                return;
            }
            await Task.Delay(500, cancellationToken);
        }

        throw new InvalidOperationException(
            "Discord's uninstaller returned, but the Windows-user Discord installation is still present. Grev Home kept its registration so the state is not falsely reported as removed.");
    }

    private static bool TryGetInstalledDiscord(out string version)
    {
        version = "current";
        var discordRoot = GetDiscordRoot();
        var updateExe = Path.Combine(discordRoot, "Update.exe");
        if (!File.Exists(updateExe)) return false;

        var appDirectories = Directory.Exists(discordRoot)
            ? Directory.EnumerateDirectories(discordRoot, "app-*")
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        foreach (var appDirectory in appDirectories)
        {
            var executable = Path.Combine(appDirectory, "Discord.exe");
            if (!File.Exists(executable)) continue;

            var info = FileVersionInfo.GetVersionInfo(executable);
            version = info.ProductVersion
                      ?? info.FileVersion
                      ?? Path.GetFileName(appDirectory).Replace("app-", string.Empty, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        return false;
    }

    private static string? ReadDiscordUninstallCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Discord");
        return key?.GetValue("QuietUninstallString") as string
               ?? key?.GetValue("UninstallString") as string;
    }

    private static async Task RunCommandLineAsync(string commandLine, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /s /c \"{commandLine}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Windows did not start Discord's registered uninstaller.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Discord's registered uninstaller exited with code {process.ExitCode}.");
        }
    }

    private static string GetDiscordRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GrevHome/0.12 DiscordInstaller");
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
}
