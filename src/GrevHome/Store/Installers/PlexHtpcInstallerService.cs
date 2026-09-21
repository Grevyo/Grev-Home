using System.Diagnostics;
using System.Net.Http;
using Microsoft.Win32;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Store.Installers;

public sealed class PlexHtpcInstallerService : ITrustedPackageInstaller, ITrustedPackageDownloadConsumer
{
    public const string InstallerId = "plex-htpc";
    public const string SupportedVersion = "1.71.1.346-f62ce923";
    private const string Sha256 = "393ceb870f9d325025fb964869ca1fbd3ac7247fae9798038ee9f726bd13a001";
    private static readonly Uri InstallerUri = new("https://downloads.plex.tv/htpc/1.71.1.346-f62ce923/windows/PlexHTPC-1.71.1.346-f62ce923-x86_64.exe");
    private static readonly HttpClient Http = TrustedInstallerSupport.CreateHttpClient("GrevHome/0.18 PlexHTPCInstaller", TimeSpan.FromMinutes(10));
    private readonly AppPaths _paths;
    private readonly InstalledAppService _installedApps;
    private TrustedPackageDownloadService? _downloads;

    public PlexHtpcInstallerService(AppPaths paths, InstalledAppService installedApps) { _paths = paths; _installedApps = installedApps; }
    string ITrustedPackageInstaller.InstallerId => InstallerId;
    public void ConfigureDownloadService(TrustedPackageDownloadService downloadService) => _downloads = downloadService;

    public Task<PackageHealthSnapshot> InspectAsync(PackageOperationContext context, CancellationToken cancellationToken = default)
    {
        Validate(context.Package);
        return Task.FromResult(TryFindInstallation(out var install)
            ? new PackageHealthSnapshot(PackageHealthState.Healthy, "Plex HTPC and its native account-owned data are available.", install.Version)
            : new PackageHealthSnapshot(PackageHealthState.RepairRecommended, "Plex HTPC is registered but its Windows executable was not found."));
    }

    public Task InstallAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
        InstallOrRepairAsync(context, progress, cancellationToken);
    public Task RepairAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
        InstallOrRepairAsync(context, progress, cancellationToken);
    public Task UpdateAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Plex HTPC owns its native update lifecycle.");
    public Task UninstallAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Remove Plex HTPC from individual GrevID libraries. Machine uninstall remains available through Windows Installed Apps.");

    private async Task InstallOrRepairAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress, CancellationToken cancellationToken)
    {
        Validate(context.Package);
        if (TryFindInstallation(out var existing))
        {
            await RegisterAsync(context.Package, existing, cancellationToken);
            progress?.Report(new PackageInstallProgress("Complete", $"Existing Plex HTPC {existing.Version} was added to Grev Home.", 100));
            return;
        }

        var staging = Path.Combine(Path.GetTempPath(), "GrevHome", "Installers", "plex-htpc", Guid.NewGuid().ToString("N"));
        var installer = Path.Combine(staging, "PlexHTPCSetup.exe");
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report(new PackageInstallProgress("Download", $"Downloading official Plex HTPC {SupportedVersion}…", 0));
            using var download = _downloads is null
                ? null
                : await _downloads.DownloadAsync(InstallerId, $"Plex HTPC {SupportedVersion}", InstallerUri,
                    "PlexHTPCSetup.exe", null, progress, 0, 70, cancellationToken);
            var installerPath = download?.FilePath ?? installer;
            if (download is null)
                await TrustedInstallerSupport.DownloadWindowsInstallerAsync(Http, InstallerUri, installerPath, "Plex HTPC", "Plex's download was not a plausible installer.", progress, cancellationToken);
            await TrustedInstallerSupport.VerifySha256Async(installerPath, Sha256, "Plex HTPC", cancellationToken);
            progress?.Report(new PackageInstallProgress("Install", "Installing Plex HTPC silently for Windows…", 74));
            using var process = Process.Start(new ProcessStartInfo { FileName = installerPath, Arguments = "/S", UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Windows did not start PlexHTPCSetup.exe.");
            await process.WaitForExitAsync(cancellationToken);
            for (var attempt = 0; attempt < 80 && !TryFindInstallation(out existing); attempt++) await Task.Delay(500, cancellationToken);
            if (!TryFindInstallation(out existing)) throw new InvalidOperationException($"Plex HTPC installer exited with code {process.ExitCode}, but Plex HTPC.exe was not detected.");
            await RegisterAsync(context.Package, existing, cancellationToken);
            progress?.Report(new PackageInstallProgress("Complete", "Plex HTPC is installed globally and ready for its own sign-in flow.", 100));
        }
        finally { TrustedInstallerSupport.TryDeleteDirectory(staging); }
    }

    private async Task RegisterAsync(GrevStorePackageDefinition package, PlexInstallation install, CancellationToken cancellationToken)
    {
        var app = package.App with { Launch = package.App.Launch with { Executable = install.Executable, WorkingDirectory = install.Root } };
        await _installedApps.RegisterInstalledAsync(app, install.Version, null, cancellationToken);
    }

    private static bool TryFindInstallation(out PlexInstallation installation)
    {
        var candidates = new List<string>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) AddRegistryCandidates(candidates, hive, view);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (local.Length > 0) { candidates.Add(Path.Combine(local, "Plex HTPC", "Plex HTPC.exe")); candidates.Add(Path.Combine(local, "Programs", "Plex HTPC", "Plex HTPC.exe")); }
        if (programs.Length > 0) candidates.Add(Path.Combine(programs, "Plex", "Plex HTPC", "Plex HTPC.exe"));
        foreach (var executable in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            var version = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? SupportedVersion;
            installation = new PlexInstallation(executable, Path.GetDirectoryName(executable)!, version);
            return true;
        }
        installation = null!;
        return false;
    }

    private static void AddRegistryCandidates(List<string> candidates, RegistryHive hive, RegistryView view)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) return;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(name);
                if (key is null || !(key.GetValue("DisplayName")?.ToString() ?? "").Contains("Plex HTPC", StringComparison.OrdinalIgnoreCase)) continue;
                var icon = key.GetValue("DisplayIcon")?.ToString()?.Trim('"').Split(',')[0];
                if (!string.IsNullOrWhiteSpace(icon)) candidates.Add(icon);
                var location = key.GetValue("InstallLocation")?.ToString()?.Trim('"');
                if (!string.IsNullOrWhiteSpace(location)) candidates.Add(Path.Combine(location, "Plex HTPC.exe"));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }

    private static void Validate(GrevStorePackageDefinition package)
    {
        if (!string.Equals(package.InstallerId, InstallerId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.App.AppId, "plex-htpc", StringComparison.OrdinalIgnoreCase) ||
            package.App.InstallStrategy != InstallStrategy.SystemInstalled || package.App.DataStrategy != DataStrategy.NativeAccount)
            throw new InvalidOperationException("The Plex HTPC installer can only manage the trusted global Plex HTPC package.");
    }

    private sealed record PlexInstallation(string Executable, string Root, string Version);
}
