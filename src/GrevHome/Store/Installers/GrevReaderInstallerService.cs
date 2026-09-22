using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Store.Installers;

public sealed class GrevReaderInstallerService : ITrustedPackageInstaller
{
    public const string InstallerId = "grevreader";
    public const string SupportedVersion = "0.1.0";

    private static readonly Uri StableManifestUri =
        new("https://raw.githubusercontent.com/Grevyo/GrevReader-Releases/main/stable.json");
    private static readonly HttpClient Http =
        TrustedInstallerSupport.CreateHttpClient("GrevHome/0.18 GrevReaderInstaller", TimeSpan.FromMinutes(10));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly InstalledAppService _installedApps;

    public GrevReaderInstallerService(AppPaths paths, InstalledAppService installedApps)
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

        if (!File.Exists(GetExecutablePath()))
        {
            return Task.FromResult(new PackageHealthSnapshot(
                PackageHealthState.RepairRecommended,
                "GrevReader is registered in Grev Home but its executable is missing."));
        }

        var version = FileVersionInfo.GetVersionInfo(GetExecutablePath()).ProductVersion;
        return Task.FromResult(new PackageHealthSnapshot(
            PackageHealthState.Healthy,
            "GrevReader is installed and ready.",
            NormalizeVersion(version)));
    }

    async Task ITrustedPackageInstaller.InstallAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidatePackage(context.Package);
        if (File.Exists(GetExecutablePath()))
        {
            var installedVersion = NormalizeVersion(
                FileVersionInfo.GetVersionInfo(GetExecutablePath()).ProductVersion);
            progress?.Report(new PackageInstallProgress(
                "Register",
                "GrevReader is already installed. Adding it to this Grev Home library…",
                90));
            await RegisterAsync(context.Package, installedVersion, cancellationToken);
            progress?.Report(new PackageInstallProgress(
                "Complete",
                $"GrevReader {installedVersion} is ready.",
                100));
            return;
        }

        await InstallLatestAsync(context.Package, "Install", progress, cancellationToken);
    }

    Task ITrustedPackageInstaller.UpdateAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidatePackage(context.Package);
        return InstallLatestAsync(context.Package, "Update", progress, cancellationToken);
    }

    Task ITrustedPackageInstaller.RepairAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidatePackage(context.Package);
        return InstallLatestAsync(context.Package, "Repair", progress, cancellationToken);
    }

    async Task ITrustedPackageInstaller.UninstallAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidatePackage(context.Package);
        var uninstaller = Path.Combine(GetInstallRoot(), "unins000.exe");
        if (!File.Exists(uninstaller))
        {
            throw new InvalidOperationException(
                "GrevReader's registered uninstaller was not found. Nothing was deleted; use Windows Installed Apps if manual removal is required.");
        }

        progress?.Report(new PackageInstallProgress(
            "Uninstall",
            "Uninstalling GrevReader silently…",
            20));
        await RunElevatedAsync(
            uninstaller,
            "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            cancellationToken);

        for (var attempt = 0; attempt < 60 && File.Exists(GetExecutablePath()); attempt++)
        {
            await Task.Delay(500, cancellationToken);
        }

        if (File.Exists(GetExecutablePath()))
        {
            throw new InvalidOperationException(
                "GrevReader's uninstaller finished but the application is still present.");
        }

        var registrationRoot = _paths.GetGlobalAppRoot(context.Package.App.AppId);
        if (Directory.Exists(registrationRoot))
        {
            Directory.Delete(registrationRoot, recursive: true);
        }

        progress?.Report(new PackageInstallProgress(
            "Complete",
            "GrevReader was removed. User game profiles were preserved.",
            100));
    }

    private async Task InstallLatestAsync(
        GrevStorePackageDefinition package,
        string operation,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        _paths.EnsureMachineLayout();
        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "GrevHome",
            "Installers",
            InstallerId,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        var installerPath = Path.Combine(stagingRoot, "GrevReaderSetup.exe");

        try
        {
            progress?.Report(new PackageInstallProgress(
                "Manifest",
                "Checking the public GrevReader release channel…",
                0));
            var manifest = await DownloadManifestAsync(cancellationToken);

            progress?.Report(new PackageInstallProgress(
                "Download",
                $"Downloading GrevReader {manifest.Version}…",
                5));
            await DownloadAndVerifyAsync(manifest, installerPath, progress, cancellationToken);

            progress?.Report(new PackageInstallProgress(
                operation,
                $"{operation}ing GrevReader silently…",
                78));
            await RunElevatedAsync(
                installerPath,
                "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
                cancellationToken);

            if (!File.Exists(GetExecutablePath()))
            {
                throw new InvalidOperationException(
                    "The GrevReader installer completed but GrevReader.exe was not found.");
            }

            progress?.Report(new PackageInstallProgress(
                "Register",
                "Registering GrevReader with Grev Home…",
                96));
            await RegisterAsync(package, manifest.Version, cancellationToken);
            progress?.Report(new PackageInstallProgress(
                "Complete",
                $"GrevReader {manifest.Version} is installed and ready.",
                100));
        }
        finally
        {
            TrustedInstallerSupport.TryDeleteDirectory(stagingRoot);
        }
    }

    private static async Task<GrevReaderReleaseManifest> DownloadManifestAsync(
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(StableManifestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var manifest = JsonSerializer.Deserialize<GrevReaderReleaseManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("The GrevReader release manifest was empty.");

        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.Sha256) ||
            !Uri.TryCreate(manifest.InstallerUrl, UriKind.Absolute, out var installerUri) ||
            installerUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(installerUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !installerUri.AbsolutePath.StartsWith(
                "/Grevyo/GrevReader-Releases/releases/download/",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The GrevReader release manifest failed trusted-source validation.");
        }

        if (manifest.Sha256.Length != 64 ||
            manifest.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException(
                "The GrevReader release manifest contains an invalid SHA-256 value.");
        }

        return manifest;
    }

    private static async Task DownloadAndVerifyAsync(
        GrevReaderReleaseManifest manifest,
        string destination,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(
            manifest.InstallerUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var expectedLength = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            if (expectedLength > 0)
            {
                progress?.Report(new PackageInstallProgress(
                    "Download",
                    $"Downloading GrevReader {manifest.Version}…",
                    Math.Min(70, 5 + (double)total / expectedLength.Value * 65)));
            }
        }

        await target.FlushAsync(cancellationToken);
        if (total < 1_000_000)
        {
            throw new InvalidOperationException(
                "The GrevReader download was too small to be a valid installer.");
        }

        await using var verificationStream = File.OpenRead(destination);
        var actualHash = Convert.ToHexString(
            await SHA256.HashDataAsync(verificationStream, cancellationToken));
        if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The GrevReader installer did not match its trusted SHA-256 checksum.");
        }
    }

    private async Task RegisterAsync(
        GrevStorePackageDefinition package,
        string version,
        CancellationToken cancellationToken)
    {
        await _installedApps.RegisterInstalledAsync(
            package.App,
            string.IsNullOrWhiteSpace(version) ? SupportedVersion : version,
            ownerGrevId: null,
            cancellationToken);
    }

    private static async Task RunElevatedAsync(
        string executable,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(executable)
                ?? Environment.CurrentDirectory
        }) ?? throw new InvalidOperationException(
            $"Windows did not start {Path.GetFileName(executable)}.");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(executable)} exited with code {process.ExitCode}.");
        }
    }

    private static void ValidatePackage(GrevStorePackageDefinition package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!string.Equals(package.InstallerId, InstallerId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.App.AppId, InstallerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The GrevReader installer can only manage the trusted GrevReader package.");
        }

        if (package.App.InstallStrategy != InstallStrategy.SystemInstalled ||
            package.App.DataStrategy != DataStrategy.NativeAccount)
        {
            throw new InvalidOperationException(
                "GrevReader must remain a machine-wide system install with Windows-user profile data.");
        }
    }

    private static string GetInstallRoot() => @"C:\GrevCo\GrevReader";
    private static string GetExecutablePath() => Path.Combine(GetInstallRoot(), "GrevReader.exe");

    private static string NormalizeVersion(string? version)
    {
        if (Version.TryParse(version, out var parsed))
        {
            return parsed.Build > 0
                ? $"{parsed.Major}.{parsed.Minor}.{parsed.Build}"
                : $"{parsed.Major}.{parsed.Minor}.{Math.Max(0, parsed.Build)}";
        }

        return string.IsNullOrWhiteSpace(version) ? SupportedVersion : version;
    }

    private sealed record GrevReaderReleaseManifest(
        string Version,
        string InstallerUrl,
        string Sha256);
}
