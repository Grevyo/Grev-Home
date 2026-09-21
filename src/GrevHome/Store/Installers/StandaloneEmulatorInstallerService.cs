using System.IO;
using System.Net.Http;
using System.Text;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Store.Installers;

public sealed record StandaloneEmulatorPackageSpec(
    string AppId,
    string DisplayName,
    string Version,
    Uri ArchiveUri,
    string ArchiveFileName,
    string Sha256,
    string ExecutableName,
    string? BaseArguments,
    IReadOnlyList<string> MutableBinaryEntries,
    Action<string, string, string, string> Configure);

/// <summary>Trusted portable installer shared by the standalone emulator packages.</summary>
public sealed class StandaloneEmulatorInstallerService : ITrustedPackageInstaller, ITrustedPackageDownloadConsumer
{
    private static readonly HttpClient Http = TrustedInstallerSupport.CreateHttpClient(
        "GrevHome/0.17 StandaloneEmulators", TimeSpan.FromMinutes(12));

    private readonly AppPaths _paths;
    private readonly InstalledAppService _installedApps;
    private readonly MachineDefaultsService _machineDefaults;
    private readonly StandaloneEmulatorPackageSpec _spec;
    private TrustedPackageDownloadService? _downloads;

    public StandaloneEmulatorInstallerService(AppPaths paths, InstalledAppService installedApps,
        MachineDefaultsService machineDefaults, StandaloneEmulatorPackageSpec spec)
    {
        _paths = paths;
        _installedApps = installedApps;
        _machineDefaults = machineDefaults;
        _spec = spec;
    }

    public string InstallerId => "standalone-" + _spec.AppId;
    public void ConfigureDownloadService(TrustedPackageDownloadService downloadService) => _downloads = downloadService;

    public async Task<PackageHealthSnapshot> InspectAsync(PackageOperationContext context, CancellationToken cancellationToken = default)
    {
        Validate(context.Package);
        var grevId = TrustedInstallerSupport.RequireGrevId(context);
        var executable = Path.Combine(_paths.GetProfileAppRoot(grevId, _spec.AppId), _spec.ExecutableName);
        if (!File.Exists(executable))
            return new PackageHealthSnapshot(PackageHealthState.RepairRecommended,
                $"{_spec.DisplayName} is registered but its executable is missing.", _spec.Version);

        var dataRoot = _paths.GetProfileAppDataRoot(grevId, _spec.AppId);
        if (!Directory.Exists(dataRoot))
            return new PackageHealthSnapshot(PackageHealthState.RepairRecommended,
                $"{_spec.DisplayName}'s GrevID-owned configuration folder is missing. Repair can recreate it without affecting shared games or BIOS files.", _spec.Version);

        var biosRoot = await _machineDefaults.GetBiosRootAsync(cancellationToken);
        var readiness = StandaloneEmulatorReadiness.Inspect(_spec.AppId, Path.GetDirectoryName(executable)!, dataRoot, biosRoot);
        return readiness.IsReady
            ? new PackageHealthSnapshot(PackageHealthState.Healthy,
                $"{_spec.DisplayName} binaries, isolated GrevID configuration and controller-first defaults are ready.", _spec.Version)
            : new PackageHealthSnapshot(PackageHealthState.SetupRequired, readiness.StoreMessage!, _spec.Version);
    }

    public Task InstallAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default) => InstallOrReplaceAsync(context, progress, false, cancellationToken);

    public Task UpdateAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default) => InstallOrReplaceAsync(context, progress, true, cancellationToken);

    public Task RepairAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default) => InstallOrReplaceAsync(context, progress, true, cancellationToken);

    public async Task UninstallAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Validate(context.Package);
        var grevId = TrustedInstallerSupport.RequireGrevId(context);
        cancellationToken.ThrowIfCancellationRequested();
        var binaryRoot = _paths.GetProfileAppRoot(grevId, _spec.AppId);
        if (Directory.Exists(binaryRoot)) Directory.Delete(binaryRoot, recursive: true);
        progress?.Report(new PackageInstallProgress("Complete",
            $"{_spec.DisplayName} binaries were removed. GrevID configuration and saves were preserved.", 100));
        await Task.CompletedTask;
    }

    private async Task InstallOrReplaceAsync(PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress, bool replace, CancellationToken cancellationToken)
    {
        Validate(context.Package);
        var grevId = TrustedInstallerSupport.RequireGrevId(context);
        _paths.EnsureProfileLayout(grevId);
        var targetRoot = _paths.GetProfileAppRoot(grevId, _spec.AppId);
        var dataRoot = _paths.GetProfileAppDataRoot(grevId, _spec.AppId);
        if (!replace && Directory.Exists(targetRoot) && Directory.EnumerateFileSystemEntries(targetRoot).Any())
            throw new InvalidOperationException($"{_spec.DisplayName} already has files in its GrevID app folder. Use Repair instead.");

        var staging = Path.Combine(Path.GetTempPath(), "GrevHome", "Installers", _spec.AppId, Guid.NewGuid().ToString("N"));
        var archive = Path.Combine(staging, _spec.ArchiveFileName);
        var extracted = Path.Combine(staging, "extracted");
        var replacement = Path.Combine(staging, "replacement");
        Directory.CreateDirectory(extracted);
        try
        {
            progress?.Report(new PackageInstallProgress("Download", $"Downloading official {_spec.DisplayName} {_spec.Version}…", 0));
            await TrustedInstallerSupport.DownloadArchiveAsync(Http, _downloads, InstallerId,
                $"{_spec.DisplayName} {_spec.Version}", _spec.ArchiveUri, _spec.ArchiveFileName, grevId,
                archive, progress, 0, 72, _spec.DisplayName, cancellationToken);
            progress?.Report(new PackageInstallProgress("Verify", $"Verifying {_spec.DisplayName} SHA-256…", 74));
            await TrustedInstallerSupport.VerifySha256Async(archive, _spec.Sha256, _spec.DisplayName, cancellationToken);
            await TrustedInstallerSupport.ValidateArchiveEntriesAsync(archive, _spec.DisplayName, cancellationToken);
            await TrustedInstallerSupport.ExtractArchiveAsync(archive, extracted, _spec.DisplayName, cancellationToken);

            var executable = Directory.EnumerateFiles(extracted, _spec.ExecutableName, SearchOption.AllDirectories).SingleOrDefault()
                ?? throw new InvalidDataException($"The verified {_spec.DisplayName} archive did not contain {_spec.ExecutableName}.");
            var packageRoot = Path.GetDirectoryName(executable)!;
            Directory.Move(packageRoot, replacement);
            PreserveMutableEntries(targetRoot, replacement);

            var backup = targetRoot + ".grev-backup-" + Guid.NewGuid().ToString("N");
            if (Directory.Exists(targetRoot)) Directory.Move(targetRoot, backup);
            try
            {
                Directory.Move(replacement, targetRoot);
                Directory.CreateDirectory(dataRoot);
                var games = await _machineDefaults.GetGamesRootAsync(cancellationToken);
                var bios = await _machineDefaults.GetBiosRootAsync(cancellationToken);
                Directory.CreateDirectory(games);
                Directory.CreateDirectory(bios);
                _spec.Configure(targetRoot, dataRoot, games, bios);
                await _installedApps.RegisterInstalledAsync(context.Package.App, _spec.Version, grevId, cancellationToken);
                TrustedInstallerSupport.TryDeleteDirectory(backup);
            }
            catch
            {
                TrustedInstallerSupport.TryDeleteDirectory(targetRoot);
                if (Directory.Exists(backup)) Directory.Move(backup, targetRoot);
                throw;
            }
            progress?.Report(new PackageInstallProgress("Complete", $"{_spec.DisplayName} is silently installed and configured for this GrevID.", 100));
        }
        finally { TrustedInstallerSupport.TryDeleteDirectory(staging); }
    }

    private void PreserveMutableEntries(string oldRoot, string newRoot)
    {
        if (!Directory.Exists(oldRoot)) return;
        foreach (var name in _spec.MutableBinaryEntries)
        {
            var source = Path.Combine(oldRoot, name);
            var destination = Path.Combine(newRoot, name);
            if (Directory.Exists(source)) CopyDirectory(source, destination);
            else if (File.Exists(source)) { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, true); }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private void Validate(GrevStorePackageDefinition package)
    {
        if (!string.Equals(package.App.AppId, _spec.AppId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.InstallerId, InstallerId, StringComparison.OrdinalIgnoreCase) ||
            package.App.InstallStrategy != InstallStrategy.GrevIdPortable)
            throw new InvalidOperationException($"This installer can only manage the trusted {_spec.DisplayName} Profile App.");
    }
}
