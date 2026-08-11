using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Store.Installers;

public sealed record PackageInstallProgress(
    string Stage,
    string Message,
    double? Percent = null);

/// <summary>
/// Trusted, RetroArch-specific package workflow. Grev Home downloads the official portable
/// archive, verifies the pinned hash, installs only inside the owning GrevID app root, keeps
/// profile data outside the binary root, and removes only that verified binary install during
/// uninstall.
/// </summary>
public sealed class RetroArchInstallerService
{
    public const string InstallerId = "retroarch";
    public const string SupportedVersion = "1.22.2";
    public const string SupportedArchiveSha256 = "B2139B1D0F9D4526DC6B5CE23CBB3EFDC766096FA6F2C3DF016818B486AC6372";

    private static readonly Uri ArchiveUri = new(
        $"https://buildbot.libretro.com/stable/{SupportedVersion}/windows/x86_64/RetroArch.7z");

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly AppPaths _paths;
    private readonly InstalledAppService _installedApps;

    public RetroArchInstallerService(AppPaths paths, InstalledAppService installedApps)
    {
        _paths = paths;
        _installedApps = installedApps;
    }

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

        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "GrevHome",
            "Installers",
            "retroarch",
            Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(stagingRoot, "RetroArch.7z");
        var extractRoot = Path.Combine(stagingRoot, "extract");

        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(extractRoot);

        try
        {
            progress?.Report(new PackageInstallProgress("Download", $"Downloading RetroArch {SupportedVersion}...", 0));
            await DownloadArchiveAsync(archivePath, progress, cancellationToken);

            progress?.Report(new PackageInstallProgress("Verify", "Verifying pinned SHA-256...", 72));
            await VerifySha256Async(archivePath, SupportedArchiveSha256, cancellationToken);

            progress?.Report(new PackageInstallProgress("Extract", "Checking archive paths...", 76));
            await ValidateArchiveEntriesAsync(archivePath, cancellationToken);

            progress?.Report(new PackageInstallProgress("Extract", "Extracting RetroArch without opening a setup window...", 80));
            await ExtractArchiveAsync(archivePath, extractRoot, cancellationToken);

            var extractedRoot = FindExtractedRetroArchRoot(extractRoot);
            var executable = Path.Combine(extractedRoot, "retroarch.exe");
            if (!File.Exists(executable))
            {
                throw new InvalidDataException("The verified RetroArch archive did not contain retroarch.exe in the expected package layout.");
            }

            progress?.Report(new PackageInstallProgress("Install", "Moving verified files into this profile...", 90));
            MoveExtractedPackage(extractedRoot, targetRoot);

            progress?.Report(new PackageInstallProgress("Configure", "Creating profile-owned RetroArch folders and defaults...", 94));
            ConfigureProfile(grevId);

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
        if (!PathsEqual(installed.BinaryRoot, targetRoot))
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

    private void ConfigureProfile(string grevId)
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

        Directory.CreateDirectory(appDataRoot);
        Directory.CreateDirectory(saveRamRoot);
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

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

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
                var overallPercent = downloadPercent * 0.70;
                progress?.Report(new PackageInstallProgress(
                    "Download",
                    $"{FormatBytes(received)} / {FormatBytes(total.Value)}",
                    overallPercent));
            }
        }

        await output.FlushAsync(cancellationToken);
        if (received <= 0)
        {
            throw new InvalidDataException("RetroArch download completed with no data.");
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
            throw new InvalidDataException("RetroArch download failed SHA-256 verification. Nothing was installed.");
        }
    }

    private static async Task ValidateArchiveEntriesAsync(string archivePath, CancellationToken cancellationToken)
    {
        var result = await RunTarAsync(["-tf", archivePath], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException(
                "Windows could not read the verified RetroArch portable archive. " +
                TrimProcessError(result.StandardError));
        }

        var entries = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
        {
            throw new InvalidDataException("The RetroArch archive contains no files.");
        }

        foreach (var entry in entries)
        {
            var normalized = entry.Replace('\\', '/');
            if (normalized.StartsWith('/') ||
                normalized.Contains(':') ||
                normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == ".."))
            {
                throw new InvalidDataException("RetroArch archive contains an unsafe path. Nothing was extracted.");
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
                "Windows could not extract the verified RetroArch portable archive. " +
                TrimProcessError(result.StandardError));
        }
    }

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

    private static void MoveExtractedPackage(string extractedRoot, string targetRoot)
    {
        if (Directory.Exists(targetRoot))
        {
            if (Directory.EnumerateFileSystemEntries(targetRoot).Any())
            {
                throw new InvalidOperationException("RetroArch target folder stopped being empty during installation.");
            }

            Directory.Delete(targetRoot);
        }

        Directory.Move(extractedRoot, targetRoot);
    }

    private static string EscapeConfigPath(string path) =>
        path.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static async Task<TarResult> RunTarAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var tarPath = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(tarPath))
        {
            throw new FileNotFoundException(
                "Windows tar.exe is required for the controller-only RetroArch portable installer.",
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GrevHome/0.11");
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
