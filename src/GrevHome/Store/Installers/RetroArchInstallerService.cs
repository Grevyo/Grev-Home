using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Store.Installers;

/// <summary>
/// Trusted, RetroArch-specific package workflow. Grev Home downloads the official portable
/// archive, verifies the pinned hash, installs only inside the owning GrevID app root, keeps
/// profile data outside the binary root, and removes only that verified binary install during
/// uninstall. Update and repair replace only the package-owned binary root and preserve the
/// GrevID-owned configuration/save roots.
/// </summary>
public sealed class RetroArchInstallerService : ITrustedPackageInstaller, ITrustedPackageDownloadConsumer
{
    public const string InstallerId = "retroarch";
    public const string SupportedVersion = "1.22.2";
    public const string SupportedArchiveSha256 = "B2139B1D0F9D4526DC6B5CE23CBB3EFDC766096FA6F2C3DF016818B486AC6372";

    private static readonly Uri ArchiveUri = new(
        $"https://buildbot.libretro.com/stable/{SupportedVersion}/windows/x86_64/RetroArch.7z");

    private static readonly HttpClient Http =
        TrustedInstallerSupport.CreateHttpClient("GrevHome/0.12", TimeSpan.FromMinutes(30));

    private readonly AppPaths _paths;
    private readonly InstalledAppService _installedApps;
    private readonly MachineDefaultsService _machineDefaults;
    private TrustedPackageDownloadService? _downloadService;

    public RetroArchInstallerService(AppPaths paths, InstalledAppService installedApps, MachineDefaultsService machineDefaults)
    {
        _paths = paths;
        _installedApps = installedApps;
        _machineDefaults = machineDefaults;
    }

    public void ConfigureDownloadService(TrustedPackageDownloadService downloadService) =>
        _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));

    string ITrustedPackageInstaller.InstallerId => InstallerId;

    public Task<PackageHealthSnapshot> InspectAsync(
        PackageOperationContext context,
        CancellationToken cancellationToken = default)
    {
        var grevId = TrustedInstallerSupport.RequireGrevId(context);
        ValidatePackage(context.Package, grevId);
        cancellationToken.ThrowIfCancellationRequested();

        var binaryRoot = _paths.GetProfileAppRoot(grevId, context.Package.App.AppId);
        var executable = Path.Combine(binaryRoot, "retroarch.exe");
        if (!Directory.Exists(binaryRoot) || !File.Exists(executable))
        {
            return Task.FromResult(new PackageHealthSnapshot(
                PackageHealthState.RepairRecommended,
                "The RetroArch registration exists but its profile-owned executable is missing.",
                SupportedVersion));
        }

        var config = Path.Combine(_paths.GetProfileAppDataRoot(grevId, context.Package.App.AppId), "retroarch.cfg");
        if (!File.Exists(config))
        {
            return Task.FromResult(new PackageHealthSnapshot(
                PackageHealthState.RepairRecommended,
                "RetroArch binaries exist but the GrevID-owned configuration is missing. Repair can recreate the profile defaults without deleting saves.",
                SupportedVersion));
        }

        return Task.FromResult(new PackageHealthSnapshot(
            PackageHealthState.Healthy,
            "RetroArch binaries and the GrevID-owned configuration are present.",
            SupportedVersion));
    }

    Task ITrustedPackageInstaller.InstallAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        InstallAsync(context.Package, TrustedInstallerSupport.RequireGrevId(context), progress, cancellationToken);

    Task ITrustedPackageInstaller.UpdateAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        ReplaceBinaryPackageAsync(context.Package, TrustedInstallerSupport.RequireGrevId(context), "Update", progress, cancellationToken);

    Task ITrustedPackageInstaller.RepairAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        ReplaceBinaryPackageAsync(context.Package, TrustedInstallerSupport.RequireGrevId(context), "Repair", progress, cancellationToken);

    Task ITrustedPackageInstaller.UninstallAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        UninstallAsync(context.Package, TrustedInstallerSupport.RequireGrevId(context), progress, cancellationToken);

    public async Task InstallAsync(
        GrevStorePackageDefinition package,
        string grevId,
        IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(package, grevId);

        _paths.EnsureProfileLayout(grevId);
        var targetRoot = _paths.GetProfileAppRoot(grevId, package.App.AppId);
        if (Directory.Exists(targetRoot) && Directory.EnumerateFileSystemEntries(targetRoot).Any())
        {
            throw new InvalidOperationException(
                "The RetroArch app folder already contains files. Grev Home will not overwrite an unverified existing folder during a fresh install.");
        }

        var stagingRoot = CreateStagingRoot();
        var archivePath = Path.Combine(stagingRoot, "RetroArch.7z");
        var extractRoot = Path.Combine(stagingRoot, "extract");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(extractRoot);

        try
        {
            var extractedRoot = await PrepareVerifiedPackageAsync(archivePath, extractRoot, grevId, progress, cancellationToken);

            progress?.Report(new PackageInstallProgress("Install", "Moving verified files into this profile...", 90));
            TrustedInstallerSupport.MoveExtractedPackage(extractedRoot, targetRoot, "RetroArch");

            progress?.Report(new PackageInstallProgress("Configure", "Creating profile-owned RetroArch folders and defaults...", 94));
            await ConfigureProfileAsync(grevId, cancellationToken);

            progress?.Report(new PackageInstallProgress("Register", "Registering RetroArch with Grev Home...", 98));
            await _installedApps.RegisterInstalledAsync(
                package.App,
                SupportedVersion,
                grevId,
                cancellationToken);

            progress?.Report(new PackageInstallProgress("Complete", $"RetroArch {SupportedVersion} is installed for this profile.", 100));
        }
        catch
        {
            // A failed fresh install must never leave a registered half-install. Only the
            // package-owned binary root is rolled back. Persistent profile data is outside it.
            TrustedInstallerSupport.TryDeleteDirectory(targetRoot);
            throw;
        }
        finally
        {
            TrustedInstallerSupport.TryDeleteDirectory(stagingRoot);
        }
    }

    public async Task UninstallAsync(
        GrevStorePackageDefinition package,
        string grevId,
        IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(package, grevId);

        progress?.Report(new PackageInstallProgress("Uninstall", "Verifying this GrevID-owned RetroArch installation...", 10));
        var entries = await _installedApps.GetInstalledForUserAsync(grevId, cancellationToken);
        var installed = entries.FirstOrDefault(entry =>
            string.Equals(entry.Manifest.Definition.AppId, package.App.AppId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Manifest.OwnerGrevId, grevId, StringComparison.OrdinalIgnoreCase));

        if (installed is null)
        {
            throw new InvalidOperationException("RetroArch is not registered as installed for the current Primary GrevID.");
        }

        var targetRoot = _paths.GetProfileAppRoot(grevId, package.App.AppId);
        if (!TrustedInstallerSupport.PathsEqual(installed.BinaryRoot, targetRoot))
        {
            throw new InvalidOperationException("The registered RetroArch binary path does not match the current GrevID app root. Nothing was removed.");
        }

        progress?.Report(new PackageInstallProgress("Preserve", "Preserving profile configuration and saves...", 35));
        PreserveLegacyBinaryConfig(targetRoot, grevId);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new PackageInstallProgress("Uninstall", "Removing only this profile's RetroArch binaries...", 70));
        if (Directory.Exists(targetRoot))
        {
            await Task.Run(() => Directory.Delete(targetRoot, recursive: true), cancellationToken);
        }

        if (Directory.Exists(targetRoot))
        {
            throw new IOException("RetroArch binary removal did not complete. Profile data was not deleted.");
        }

        progress?.Report(new PackageInstallProgress(
            "Complete",
            "RetroArch binaries were removed. Profile configuration, saves, states, screenshots, remaps and playlists were preserved.",
            100));
    }

    private async Task ReplaceBinaryPackageAsync(
        GrevStorePackageDefinition package,
        string grevId,
        string operation,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidatePackage(package, grevId);
        _paths.EnsureProfileLayout(grevId);

        var targetRoot = _paths.GetProfileAppRoot(grevId, package.App.AppId);
        var backupRoot = targetRoot + $".grev-backup-{Guid.NewGuid():N}";
        var stagingRoot = CreateStagingRoot();
        var archivePath = Path.Combine(stagingRoot, "RetroArch.7z");
        var extractRoot = Path.Combine(stagingRoot, "extract");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(extractRoot);

        var oldRootMoved = false;
        try
        {
            progress?.Report(new PackageInstallProgress(operation, "Preserving GrevID-owned configuration and saves before replacing binaries…", 2));
            PreserveLegacyBinaryConfig(targetRoot, grevId);

            var extractedRoot = await PrepareVerifiedPackageAsync(archivePath, extractRoot, grevId, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new PackageInstallProgress(operation, "Swapping the verified RetroArch binary package…", 90));
            if (Directory.Exists(targetRoot))
            {
                Directory.Move(targetRoot, backupRoot);
                oldRootMoved = true;
            }

            Directory.Move(extractedRoot, targetRoot);
            await ConfigureProfileAsync(grevId, cancellationToken);

            await _installedApps.RegisterInstalledAsync(
                package.App,
                SupportedVersion,
                grevId,
                cancellationToken);

            TrustedInstallerSupport.TryDeleteDirectory(backupRoot);
            oldRootMoved = false;
            progress?.Report(new PackageInstallProgress(
                "Complete",
                $"RetroArch {SupportedVersion} {operation.ToLowerInvariant()} completed. Profile configuration, saves and states were preserved.",
                100));
        }
        catch
        {
            // A replacement is transactional at the package-owned binary-root level. If the new
            // verified package cannot be committed, restore the old binaries when possible.
            TrustedInstallerSupport.TryDeleteDirectory(targetRoot);
            if (oldRootMoved && Directory.Exists(backupRoot) && !Directory.Exists(targetRoot))
            {
                Directory.Move(backupRoot, targetRoot);
                oldRootMoved = false;
            }
            throw;
        }
        finally
        {
            TrustedInstallerSupport.TryDeleteDirectory(stagingRoot);
            if (!oldRootMoved)
            {
                TrustedInstallerSupport.TryDeleteDirectory(backupRoot);
            }
        }
    }

    private async Task<string> PrepareVerifiedPackageAsync(
        string archivePath,
        string extractRoot,
        string grevId,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new PackageInstallProgress("Download", $"Downloading RetroArch {SupportedVersion}...", 0));
        await TrustedInstallerSupport.DownloadArchiveAsync(
            Http,
            _downloadService,
            InstallerId,
            $"RetroArch {SupportedVersion}",
            ArchiveUri,
            "RetroArch.7z",
            grevId,
            archivePath,
            progress,
            progressStart: 0,
            progressEnd: 70,
            "RetroArch",
            cancellationToken);

        progress?.Report(new PackageInstallProgress("Verify", "Verifying pinned SHA-256...", 72));
        await TrustedInstallerSupport.VerifySha256Async(archivePath, SupportedArchiveSha256, "RetroArch", cancellationToken);

        progress?.Report(new PackageInstallProgress("Extract", "Checking archive paths...", 76));
        await TrustedInstallerSupport.ValidateArchiveEntriesAsync(archivePath, "RetroArch", cancellationToken);

        progress?.Report(new PackageInstallProgress("Extract", "Extracting RetroArch without opening a setup window...", 80));
        await TrustedInstallerSupport.ExtractArchiveAsync(archivePath, extractRoot, "RetroArch", cancellationToken);

        var extractedRoot = FindExtractedRetroArchRoot(extractRoot);
        var executable = Path.Combine(extractedRoot, "retroarch.exe");
        if (!File.Exists(executable))
        {
            throw new InvalidDataException("The verified RetroArch archive did not contain retroarch.exe in the expected package layout.");
        }

        return extractedRoot;
    }

    private static void ValidatePackage(GrevStorePackageDefinition package, string grevId)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!string.Equals(package.InstallerId, InstallerId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.App.AppId, "retroarch", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The RetroArch installer can only manage the trusted RetroArch package.");
        }

        if (!package.IsProfileInstall)
        {
            throw new InvalidOperationException("RetroArch must be managed as a Profile App.");
        }

        if (string.IsNullOrWhiteSpace(grevId))
        {
            throw new InvalidOperationException("A persistent Primary GrevID is required to manage RetroArch.");
        }
    }

    private async Task ConfigureProfileAsync(string grevId, CancellationToken cancellationToken)
    {
        var appDataRoot = _paths.GetProfileAppDataRoot(grevId, "retroarch");
        var profileRoot = Directory.GetParent(Directory.GetParent(appDataRoot)?.FullName ?? string.Empty)?.FullName;
        if (string.IsNullOrWhiteSpace(profileRoot))
        {
            throw new InvalidOperationException("RetroArch profile data path is invalid.");
        }

        var saveRoot = Path.Combine(profileRoot, "Saves", "retroarch");
        var saveRamRoot = Path.Combine(saveRoot, "SaveRAM");
        var stateRoot = Path.Combine(saveRoot, "States");
        var screenshotRoot = Path.Combine(profileRoot, "Screenshots", "RetroArch");
        var remapRoot = Path.Combine(appDataRoot, "remaps");
        var playlistRoot = Path.Combine(appDataRoot, "playlists");

        // Games and BIOS/system files are machine-wide, not per-profile - every profile's RetroArch
        // is pointed at the same two folders chosen during first-run setup, so a game or BIOS file
        // only ever needs to be placed once. See MachineDefaultsService.
        var gamesRoot = await _machineDefaults.GetGamesRootAsync(cancellationToken);
        var biosRoot = await _machineDefaults.GetBiosRootAsync(cancellationToken);

        Directory.CreateDirectory(appDataRoot);
        Directory.CreateDirectory(saveRamRoot);
        Directory.CreateDirectory(gamesRoot);
        Directory.CreateDirectory(biosRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(screenshotRoot);
        Directory.CreateDirectory(remapRoot);
        Directory.CreateDirectory(playlistRoot);

        var configPath = Path.Combine(appDataRoot, "retroarch.cfg");
        if (File.Exists(configPath))
        {
            return;
        }

        var config = new StringBuilder()
            .AppendLine("# Generated by Grev Home for this GrevID-owned RetroArch install.")
            .AppendLine($"# GrevID: {grevId}")
            .AppendLine($"savefile_directory = \"{EscapeConfigPath(saveRamRoot)}\"")
            .AppendLine($"savestate_directory = \"{EscapeConfigPath(stateRoot)}\"")
            .AppendLine($"screenshot_directory = \"{EscapeConfigPath(screenshotRoot)}\"")
            .AppendLine($"input_remapping_directory = \"{EscapeConfigPath(remapRoot)}\"")
            .AppendLine($"playlist_directory = \"{EscapeConfigPath(playlistRoot)}\"")
            .AppendLine($"content_directory = \"{EscapeConfigPath(gamesRoot)}\"")
            .AppendLine($"system_directory = \"{EscapeConfigPath(biosRoot)}\"")
            .AppendLine("config_save_on_exit = \"true\"")
            .AppendLine("video_fullscreen = \"true\"")
            .ToString();

        File.WriteAllText(configPath, config, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void PreserveLegacyBinaryConfig(string binaryRoot, string grevId)
    {
        var legacyConfig = Path.Combine(binaryRoot, "retroarch.cfg");
        if (!File.Exists(legacyConfig)) return;

        var appDataRoot = _paths.GetProfileAppDataRoot(grevId, "retroarch");
        Directory.CreateDirectory(appDataRoot);
        var persistentConfig = Path.Combine(appDataRoot, "retroarch.cfg");
        if (!File.Exists(persistentConfig))
        {
            File.Copy(legacyConfig, persistentConfig, overwrite: false);
        }
    }

    private static string CreateStagingRoot() => Path.Combine(
        Path.GetTempPath(),
        "GrevHome",
        "Installers",
        "retroarch",
        Guid.NewGuid().ToString("N"));

    private static string FindExtractedRetroArchRoot(string extractRoot)
    {
        if (File.Exists(Path.Combine(extractRoot, "retroarch.exe")))
        {
            return extractRoot;
        }

        var candidates = Directory
            .EnumerateFiles(extractRoot, "retroarch.exe", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates.Length == 1
            ? candidates[0]!
            : throw new InvalidDataException("RetroArch package layout was not recognised safely.");
    }

    private static string EscapeConfigPath(string path) =>
        path.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
