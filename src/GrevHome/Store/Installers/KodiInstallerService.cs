using System.Diagnostics;
using System.Net.Http;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Store.Installers;

public sealed class KodiInstallerService : ITrustedPackageInstaller, ITrustedPackageDownloadConsumer
{
    public const string InstallerId = "kodi";
    public const string SupportedVersion = "21.3";
    private const string Sha256 = "2c46c2bd60b38bb60eb0a5e3468bfd43e9235521eae2910b8c81455e46335c5c";
    private static readonly Uri InstallerUri = new("https://mirrors.kodi.tv/releases/windows/win64/kodi-21.3-Omega-x64.exe");
    private static readonly HttpClient Http = TrustedInstallerSupport.CreateHttpClient("GrevHome/0.18 KodiInstaller", TimeSpan.FromMinutes(8));
    private readonly AppPaths _paths;
    private readonly InstalledAppService _installedApps;
    private TrustedPackageDownloadService? _downloads;

    public KodiInstallerService(AppPaths paths, InstalledAppService installedApps) { _paths = paths; _installedApps = installedApps; }
    string ITrustedPackageInstaller.InstallerId => InstallerId;
    public void ConfigureDownloadService(TrustedPackageDownloadService downloadService) => _downloads = downloadService;

    public Task<PackageHealthSnapshot> InspectAsync(PackageOperationContext context, CancellationToken cancellationToken = default)
    {
        Validate(context.Package);
        var root = _paths.GetProfileAppRoot(TrustedInstallerSupport.RequireGrevId(context), "kodi");
        return Task.FromResult(File.Exists(Path.Combine(root, "kodi.exe"))
            ? new PackageHealthSnapshot(PackageHealthState.Healthy, "Kodi binaries and this GrevID's portable media profile are present.", SupportedVersion)
            : new PackageHealthSnapshot(PackageHealthState.RepairRecommended, "Kodi is registered but kodi.exe is missing.", SupportedVersion));
    }

    public Task InstallAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
        InstallOrRepairAsync(context, progress, cancellationToken);
    public Task UpdateAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
        InstallOrRepairAsync(context, progress, cancellationToken);
    public Task RepairAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
        InstallOrRepairAsync(context, progress, cancellationToken);

    public Task UninstallAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Validate(context.Package);
        var root = _paths.GetProfileAppRoot(TrustedInstallerSupport.RequireGrevId(context), "kodi");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        progress?.Report(new PackageInstallProgress("Complete", "This GrevID's Kodi binaries, library and settings were removed.", 100));
        return Task.CompletedTask;
    }

    private async Task InstallOrRepairAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress, CancellationToken cancellationToken)
    {
        Validate(context.Package);
        var grevId = TrustedInstallerSupport.RequireGrevId(context);
        _paths.EnsureProfileLayout(grevId);
        var target = _paths.GetProfileAppRoot(grevId, "kodi");
        var staging = Path.Combine(Path.GetTempPath(), "GrevHome", "Installers", "kodi", Guid.NewGuid().ToString("N"));
        var installer = Path.Combine(staging, "KodiSetup.exe");
        var replacement = Path.Combine(staging, "replacement");
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report(new PackageInstallProgress("Download", $"Downloading official Kodi {SupportedVersion}…", 0));
            using var download = _downloads is null
                ? null
                : await _downloads.DownloadAsync(InstallerId, $"Kodi {SupportedVersion}", InstallerUri,
                    "KodiSetup.exe", grevId, progress, 0, 70, cancellationToken);
            var installerPath = download?.FilePath ?? installer;
            if (download is null)
                await TrustedInstallerSupport.DownloadWindowsInstallerAsync(Http, InstallerUri, installerPath, $"Kodi {SupportedVersion}", "Kodi's download was not a plausible installer.", progress, cancellationToken);
            await TrustedInstallerSupport.VerifySha256Async(installerPath, Sha256, "Kodi", cancellationToken);
            Directory.CreateDirectory(replacement);
            var startInfo = new ProcessStartInfo { FileName = installerPath, UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add("/S");
            startInfo.ArgumentList.Add($"/D={replacement}");
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows did not start KodiSetup.exe.");
            await process.WaitForExitAsync(cancellationToken);
            if (!File.Exists(Path.Combine(replacement, "kodi.exe"))) throw new InvalidOperationException($"Kodi installer exited with code {process.ExitCode}, but kodi.exe was not found.");

            var existingPortable = Path.Combine(target, "portable_data");
            if (Directory.Exists(existingPortable)) CopyDirectory(existingPortable, Path.Combine(replacement, "portable_data"));
            var backup = target + ".grev-backup-" + Guid.NewGuid().ToString("N");
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try
            {
                Directory.Move(replacement, target);
                await _installedApps.RegisterInstalledAsync(context.Package.App, SupportedVersion, grevId, cancellationToken);
                TrustedInstallerSupport.TryDeleteDirectory(backup);
            }
            catch
            {
                TrustedInstallerSupport.TryDeleteDirectory(target);
                if (Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }
            progress?.Report(new PackageInstallProgress("Complete", "Kodi is installed with a controller-first profile isolated to this GrevID.", 100));
        }
        finally { TrustedInstallerSupport.TryDeleteDirectory(staging); }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var child in Directory.EnumerateDirectories(source)) CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
    }

    private static void Validate(GrevStorePackageDefinition package)
    {
        if (!string.Equals(package.InstallerId, InstallerId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.App.AppId, "kodi", StringComparison.OrdinalIgnoreCase) ||
            package.App.InstallStrategy != InstallStrategy.GrevIdPortable)
            throw new InvalidOperationException("The Kodi installer can only manage the trusted profile-isolated Kodi package.");
    }
}
