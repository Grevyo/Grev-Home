using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Store.Installers;

/// <summary>
/// Trusted PCSX2 Profile App workflow. Grev Home installs the official Stable Windows x64 Qt
/// portable package into the owning GrevID's replaceable binary root and launches it with
/// -datapath so PCSX2 configuration, BIOS files, memory cards and other mutable application data
/// remain in the persistent GrevID AppData root. Update/repair replace binaries only; uninstall
/// removes binaries only and deliberately preserves the GrevID data root.
/// </summary>
public sealed class PCSX2InstallerService : ITrustedPackageInstaller
{
    public const string InstallerId = "pcsx2";
    public const string SupportedVersion = "2.6.3";
    public const string SupportedArchiveSha256 = "963AE6C82BC858A09115C2455247FEB76B453862C04F60D41EF80739D802AE60";

    private static readonly Uri ArchiveUri = new(
        $"https://github.com/PCSX2/pcsx2/releases/download/v{SupportedVersion}/pcsx2-v{SupportedVersion}-windows-x64-Qt.7z");

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly AppPaths _paths;
    private readonly InstalledAppService _installedApps;

    public PCSX2InstallerService(AppPaths paths, InstalledAppService installedApps)
    {
        _paths = paths;
        _installedApps = installedApps;
    }

    string ITrustedPackageInstaller.InstallerId => InstallerId;

    public Task<PackageHealthSnapshot> InspectAsync(
        PackageOperationContext context,
        CancellationToken cancellationToken = default)
    {
        var grevId = RequireGrevId(context);
        ValidatePackage(context.Package, grevId);
        cancellationToken.ThrowIfCancellationRequested();

        var binaryRoot = _paths.GetProfileAppRoot(grevId, context.Package.App.AppId);
        var executable = Path.Combine(binaryRoot, "pcsx2-qt.exe");
        if (!Directory.Exists(binaryRoot) || !File.Exists(executable))
        {
            return Task.FromResult(new PackageHealthSnapshot(
                PackageHealthState.RepairRecommended,
                "The PCSX2 registration exists but its profile-owned executable is missing.",
                SupportedVersion));
        }

        var dataRoot = _paths.GetProfileAppDataRoot(grevId, context.Package.App.AppId);
        var biosRoot = Path.Combine(dataRoot, "bios");
        if (!Directory.Exists(dataRoot) || !Directory.Exists(biosRoot))
        {
            return Task.FromResult(new PackageHealthSnapshot(
                PackageHealthState.RepairRecommended,
                "PCSX2 binaries exist but the GrevID-owned data/BIOS folder is missing. Repair can recreate the folder structure without deleting user data.",
                SupportedVersion));
        }

        var hasBiosFiles = Directory.EnumerateFiles(biosRoot, "*", SearchOption.TopDirectoryOnly).Any();
        return Task.FromResult(new PackageHealthSnapshot(
            PackageHealthState.Healthy,
            hasBiosFiles
                ? "PCSX2 binaries and the GrevID-owned BIOS/data folder are present."
                : "PCSX2 is installed and healthy. A BIOS dumped from a PlayStation 2 you own still needs to be placed in the BIOS folder and selected in PCSX2.",
            SupportedVersion));
    }

    Task ITrustedPackageInstaller.InstallAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        InstallAsync(context.Package, RequireGrevId(context), progress, cancellationToken);

    Task ITrustedPackageInstaller.UpdateAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        ReplaceBinaryPackageAsync(context.Package, RequireGrevId(context), "Update", progress, cancellationToken);

    Task ITrustedPackageInstaller.RepairAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        ReplaceBinaryPackageAsync(context.Package, RequireGrevId(context), "Repair", progress, cancellationToken);

    Task ITrustedPackageInstaller.UninstallAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken) =>
        UninstallAsync(context.Package, RequireGrevId(context), progress, cancellationToken);

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
                "The PCSX2 app folder already contains files. Grev Home will not overwrite an unverified existing folder during a fresh install.");
        }

        var stagingRoot = CreateStagingRoot();
        var archivePath = Path.Combine(stagingRoot, $"pcsx2-v{SupportedVersion}.7z");
        var extractRoot = Path.Combine(stagingRoot, "extract");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(extractRoot);

        try
        {
            var extractedRoot = await PrepareVerifiedPackageAsync(archivePath, extractRoot, progress, cancellationToken);

            progress?.Report(new PackageInstallProgress("Install", "Moving verified PCSX2 files into this GrevID profile…", 90));
            MoveExtractedPackage(extractedRoot, targetRoot);

            progress?.Report(new PackageInstallProgress("Configure", "Creating the persistent GrevID PCSX2 data and BIOS folder…", 95));
            ConfigureProfile(grevId);

            progress?.Report(new PackageInstallProgress("Register", "Registering PCSX2 with Grev Home…", 98));
            await _installedApps.RegisterInstalledAsync(
                package.App,
                SupportedVersion,
                grevId,
                cancellationToken);

            progress?.Report(new PackageInstallProgress(
                "Complete",
                $"PCSX2 {SupportedVersion} is installed for this GrevID. Add your own dumped PS2 BIOS to the profile BIOS folder, then select it in PCSX2.",
                100));
        }
        catch
        {
            // Fresh-install rollback removes only the new package binary root. The GrevID data
            // root is persistent and is never treated as disposable package content.
            TryDeleteDirectory(targetRoot);
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    public async Task UninstallAsync(
        GrevStorePackageDefinition package,
        string grevId,
        IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePackage(package, grevId);

        progress?.Report(new PackageInstallProgress("Uninstall", "Verifying this GrevID-owned PCSX2 installation…", 10));
        var entries = await _installedApps.GetInstalledForUserAsync(grevId, cancellationToken);
        var installed = entries.FirstOrDefault(entry =>
            string.Equals(entry.Manifest.Definition.AppId, package.App.AppId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Manifest.OwnerGrevId, grevId, StringComparison.OrdinalIgnoreCase));

        if (installed is null)
        {
            throw new InvalidOperationException("PCSX2 is not registered as installed for the current Primary GrevID.");
        }

        var targetRoot = _paths.GetProfileAppRoot(grevId, package.App.AppId);
        if (!PathsEqual(installed.BinaryRoot, targetRoot))
        {
            throw new InvalidOperationException("The registered PCSX2 binary path does not match the current GrevID app root. Nothing was removed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new PackageInstallProgress(
            "Preserve",
            "Preserving this GrevID's PCSX2 BIOS, configuration, memory cards and other application data…",
            35));

        progress?.Report(new PackageInstallProgress("Uninstall", "Removing only this GrevID's PCSX2 binaries…", 70));
        if (Directory.Exists(targetRoot))
        {
            await Task.Run(() => Directory.Delete(targetRoot, recursive: true), cancellationToken);
        }

        if (Directory.Exists(targetRoot))
        {
            throw new IOException("PCSX2 binary removal did not complete. GrevID data was not deleted.");
        }

        progress?.Report(new PackageInstallProgress(
            "Complete",
            "PCSX2 binaries were removed. This GrevID's BIOS, configuration, memory cards and other PCSX2 data were preserved for a future reinstall.",
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
        ConfigureProfile(grevId);

        var targetRoot = _paths.GetProfileAppRoot(grevId, package.App.AppId);
        var backupRoot = targetRoot + $".grev-backup-{Guid.NewGuid():N}";
        var stagingRoot = CreateStagingRoot();
        var archivePath = Path.Combine(stagingRoot, $"pcsx2-v{SupportedVersion}.7z");
        var extractRoot = Path.Combine(stagingRoot, "extract");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(extractRoot);

        var oldRootMoved = false;
        try
        {
            progress?.Report(new PackageInstallProgress(
                operation,
                "Keeping GrevID PCSX2 data separate while preparing replacement binaries…",
                2));

            var extractedRoot = await PrepareVerifiedPackageAsync(archivePath, extractRoot, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new PackageInstallProgress(operation, "Swapping the verified PCSX2 binary package…", 90));
            if (Directory.Exists(targetRoot))
            {
                Directory.Move(targetRoot, backupRoot);
                oldRootMoved = true;
            }

            Directory.Move(extractedRoot, targetRoot);
            ConfigureProfile(grevId);

            await _installedApps.RegisterInstalledAsync(
                package.App,
                SupportedVersion,
                grevId,
                cancellationToken);

            TryDeleteDirectory(backupRoot);
            oldRootMoved = false;
            progress?.Report(new PackageInstallProgress(
                "Complete",
                $"PCSX2 {SupportedVersion} {operation.ToLowerInvariant()} completed. GrevID BIOS/configuration/application data were preserved.",
                100));
        }
        catch
        {
            TryDeleteDirectory(targetRoot);
            if (oldRootMoved && Directory.Exists(backupRoot) && !Directory.Exists(targetRoot))
            {
                Directory.Move(backupRoot, targetRoot);
                oldRootMoved = false;
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
            if (!oldRootMoved)
            {
                TryDeleteDirectory(backupRoot);
            }
        }
    }

    private async Task<string> PrepareVerifiedPackageAsync(
        string archivePath,
        string extractRoot,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new PackageInstallProgress("Download", $"Downloading PCSX2 Stable {SupportedVersion}…", 0));
        await DownloadArchiveAsync(archivePath, progress, cancellationToken);

        progress?.Report(new PackageInstallProgress("Verify", "Verifying the official release SHA-256…", 72));
        await VerifySha256Async(archivePath, SupportedArchiveSha256, cancellationToken);

        progress?.Report(new PackageInstallProgress("Extract", "Checking archive paths before extraction…", 76));
        await ValidateArchiveEntriesAsync(archivePath, cancellationToken);

        progress?.Report(new PackageInstallProgress("Extract", "Extracting PCSX2 without opening a setup wizard…", 80));
        await ExtractArchiveAsync(archivePath, extractRoot, cancellationToken);

        var extractedRoot = FindExtractedPCSX2Root(extractRoot);
        if (!File.Exists(Path.Combine(extractedRoot, "pcsx2-qt.exe")))
        {
            throw new InvalidDataException("The verified PCSX2 archive did not contain pcsx2-qt.exe in the expected package layout.");
        }

        return extractedRoot;
    }

    private void ConfigureProfile(string grevId)
    {
        var dataRoot = _paths.GetProfileAppDataRoot(grevId, "pcsx2");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(Path.Combine(dataRoot, "bios"));
    }

    private static string RequireGrevId(PackageOperationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.GrevId))
        {
            throw new InvalidOperationException("A persistent Primary GrevID is required to manage this Profile App.");
        }

        return context.GrevId;
    }

    private static void ValidatePackage(GrevStorePackageDefinition package, string grevId)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!string.Equals(package.InstallerId, InstallerId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.App.AppId, "pcsx2", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The PCSX2 installer can only manage the trusted PCSX2 package.");
        }

        if (!package.IsProfileInstall)
        {
            throw new InvalidOperationException("PCSX2 must be managed as a Profile App.");
        }

        if (string.IsNullOrWhiteSpace(grevId))
        {
            throw new InvalidOperationException("A persistent Primary GrevID is required to manage PCSX2.");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static string CreateStagingRoot() => Path.Combine(
        Path.GetTempPath(),
        "GrevHome",
        "Installers",
        "pcsx2",
        Guid.NewGuid().ToString("N"));

    private static async Task DownloadArchiveAsync(
        string destination,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(ArchiveUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[1024 * 1024];
        long received = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read <= 0) break;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;

            if (total is > 0)
            {
                var downloadPercent = Math.Clamp(received * 100d / total.Value, 0, 100);
                progress?.Report(new PackageInstallProgress(
                    "Download",
                    $"{FormatBytes(received)} / {FormatBytes(total.Value)}",
                    downloadPercent * 0.70));
            }
        }

        await output.FlushAsync(cancellationToken);
        if (received <= 0)
        {
            throw new InvalidDataException("PCSX2 download completed with no data.");
        }
    }

    private static async Task VerifySha256Async(
        string archivePath,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash);
        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("PCSX2 download failed SHA-256 verification. Nothing was installed.");
        }
    }

    private static async Task ValidateArchiveEntriesAsync(string archivePath, CancellationToken cancellationToken)
    {
        var result = await RunTarAsync(["-tf", archivePath], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException(
                "Windows could not read the verified PCSX2 portable archive. " +
                TrimProcessError(result.StandardError));
        }

        var entries = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
        {
            throw new InvalidDataException("The PCSX2 archive contains no files.");
        }

        foreach (var entry in entries)
        {
            var normalized = entry.Replace('\\', '/');
            if (normalized.StartsWith('/') ||
                normalized.Contains(':') ||
                normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == ".."))
            {
                throw new InvalidDataException("PCSX2 archive contains an unsafe path. Nothing was extracted.");
            }
        }
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string extractRoot,
        CancellationToken cancellationToken)
    {
        var result = await RunTarAsync(["-xf", archivePath, "-C", extractRoot], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException(
                "Windows could not extract the verified PCSX2 portable archive. " +
                TrimProcessError(result.StandardError));
        }
    }

    private static string FindExtractedPCSX2Root(string extractRoot)
    {
        if (File.Exists(Path.Combine(extractRoot, "pcsx2-qt.exe")))
        {
            return extractRoot;
        }

        var candidates = Directory
            .EnumerateFiles(extractRoot, "pcsx2-qt.exe", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates.Length == 1
            ? candidates[0]!
            : throw new InvalidDataException("PCSX2 package layout was not recognised safely.");
    }

    private static void MoveExtractedPackage(string extractedRoot, string targetRoot)
    {
        if (Directory.Exists(targetRoot))
        {
            if (Directory.EnumerateFileSystemEntries(targetRoot).Any())
            {
                throw new InvalidOperationException("PCSX2 target folder stopped being empty during installation.");
            }

            Directory.Delete(targetRoot);
        }

        Directory.Move(extractedRoot, targetRoot);
    }

    private static async Task<TarResult> RunTarAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var tarPath = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(tarPath))
        {
            throw new FileNotFoundException(
                "Windows tar.exe is required for the controller-only PCSX2 portable installer.",
                tarPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = tarPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath()
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new Win32Exception("Windows could not start its archive extractor.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new TarResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static string TrimProcessError(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? "No additional extractor error was returned."
            : trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GrevHome/0.13");
        return client;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private sealed record TarResult(int ExitCode, string StandardOutput, string StandardError);
}
