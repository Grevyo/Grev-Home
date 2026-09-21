using System.Diagnostics;
using System.Net.Http;
using Microsoft.Win32;
using GrevHome.Apps;
using GrevHome.Storage;

namespace GrevHome.Store.Installers;

public sealed record ControllerMediaInstallerSpec(
    string AppId,
    string DisplayName,
    string Version,
    Uri InstallerUri,
    string InstallerFileName,
    string Sha256,
    IReadOnlyList<string> DisplayNameMatches,
    IReadOnlyList<string> ExecutableNames,
    IReadOnlyList<string> SilentArguments,
    IReadOnlyList<string> FallbackExecutablePaths);

/// <summary>Silent, hash-pinned installer for global media clients that own their account state.</summary>
public sealed class ControllerMediaInstallerService : ITrustedPackageInstaller, ITrustedPackageDownloadConsumer
{
    private static readonly HttpClient Http = TrustedInstallerSupport.CreateHttpClient(
        "GrevHome/0.18 ControllerMedia", TimeSpan.FromMinutes(12));
    private readonly InstalledAppService _installedApps;
    private readonly ControllerMediaInstallerSpec _spec;
    private TrustedPackageDownloadService? _downloads;

    public ControllerMediaInstallerService(AppPaths paths, InstalledAppService installedApps, ControllerMediaInstallerSpec spec)
    {
        _installedApps = installedApps;
        _spec = spec;
    }

    public string InstallerId => "media-" + _spec.AppId;
    public void ConfigureDownloadService(TrustedPackageDownloadService downloadService) => _downloads = downloadService;

    public Task<PackageHealthSnapshot> InspectAsync(PackageOperationContext context, CancellationToken cancellationToken = default)
    {
        Validate(context.Package);
        return Task.FromResult(TryFindInstallation(out var install)
            ? new PackageHealthSnapshot(PackageHealthState.Healthy,
                $"{_spec.DisplayName} and its native account-owned data are available.", install.Version)
            : new PackageHealthSnapshot(PackageHealthState.RepairRecommended,
                $"{_spec.DisplayName} is registered but its Windows executable was not found."));
    }

    public Task InstallAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default) => InstallOrRepairAsync(context, progress, cancellationToken);
    public Task RepairAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default) => InstallOrRepairAsync(context, progress, cancellationToken);
    public Task UpdateAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default) => throw new InvalidOperationException($"{_spec.DisplayName} owns its native update lifecycle.");
    public Task UninstallAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default) => throw new InvalidOperationException(
        $"Remove {_spec.DisplayName} from individual GrevID libraries. Machine uninstall remains available through Windows Installed Apps.");

    private async Task InstallOrRepairAsync(PackageOperationContext context, IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Validate(context.Package);
        if (TryFindInstallation(out var existing))
        {
            await RegisterAsync(context.Package, existing, cancellationToken);
            progress?.Report(new PackageInstallProgress("Complete", $"Existing {_spec.DisplayName} {existing.Version} was added to Grev Home.", 100));
            return;
        }

        var staging = Path.Combine(Path.GetTempPath(), "GrevHome", "Installers", _spec.AppId, Guid.NewGuid().ToString("N"));
        var installer = Path.Combine(staging, _spec.InstallerFileName);
        Directory.CreateDirectory(staging);
        try
        {
            using var download = _downloads is null ? null : await _downloads.DownloadAsync(
                InstallerId, $"{_spec.DisplayName} {_spec.Version}", _spec.InstallerUri, _spec.InstallerFileName,
                null, progress, 0, 70, cancellationToken);
            var installerPath = download?.FilePath ?? installer;
            if (download is null)
                await TrustedInstallerSupport.DownloadWindowsInstallerAsync(Http, _spec.InstallerUri, installerPath,
                    _spec.DisplayName, $"{_spec.DisplayName}'s download was not a plausible installer.", progress, cancellationToken);
            await TrustedInstallerSupport.VerifySha256Async(installerPath, _spec.Sha256, _spec.DisplayName, cancellationToken);

            progress?.Report(new PackageInstallProgress("Install", $"Installing {_spec.DisplayName} silently for Windows…", 74));
            var startInfo = new ProcessStartInfo { FileName = installerPath, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in _spec.SilentArguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Windows did not start {_spec.InstallerFileName}.");
            await process.WaitForExitAsync(cancellationToken);
            for (var attempt = 0; attempt < 80 && !TryFindInstallation(out existing); attempt++)
                await Task.Delay(500, cancellationToken);
            if (!TryFindInstallation(out existing))
                throw new InvalidOperationException($"{_spec.DisplayName} installer exited with code {process.ExitCode}, but its executable was not detected.");
            await RegisterAsync(context.Package, existing, cancellationToken);
            progress?.Report(new PackageInstallProgress("Complete", $"{_spec.DisplayName} is installed globally and ready for its own sign-in flow.", 100));
        }
        finally { TrustedInstallerSupport.TryDeleteDirectory(staging); }
    }

    private async Task RegisterAsync(GrevStorePackageDefinition package, MediaInstallation install, CancellationToken cancellationToken)
    {
        var app = package.App with { Launch = package.App.Launch with { Executable = install.Executable, WorkingDirectory = install.Root } };
        await _installedApps.RegisterInstalledAsync(app, install.Version, null, cancellationToken);
    }

    private bool TryFindInstallation(out MediaInstallation installation)
    {
        var candidates = new List<string>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) AddRegistryCandidates(candidates, hive, view);
        candidates.AddRange(_spec.FallbackExecutablePaths.Select(Environment.ExpandEnvironmentVariables));
        foreach (var executable in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            if (!_spec.ExecutableNames.Contains(Path.GetFileName(executable), StringComparer.OrdinalIgnoreCase)) continue;
            var version = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? _spec.Version;
            installation = new MediaInstallation(executable, Path.GetDirectoryName(executable)!, version);
            return true;
        }
        installation = null!;
        return false;
    }

    private void AddRegistryCandidates(List<string> candidates, RegistryHive hive, RegistryView view)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) return;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(name);
                if (key is null) continue;
                var displayName = key.GetValue("DisplayName")?.ToString() ?? "";
                if (!_spec.DisplayNameMatches.Any(match => displayName.Contains(match, StringComparison.OrdinalIgnoreCase))) continue;
                var icon = key.GetValue("DisplayIcon")?.ToString()?.Trim('"').Split(',')[0];
                if (!string.IsNullOrWhiteSpace(icon)) candidates.Add(icon);
                var location = key.GetValue("InstallLocation")?.ToString()?.Trim('"');
                if (string.IsNullOrWhiteSpace(location)) continue;
                candidates.AddRange(_spec.ExecutableNames.Select(executable => Path.Combine(location, executable)));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }

    private void Validate(GrevStorePackageDefinition package)
    {
        if (!string.Equals(package.InstallerId, InstallerId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.App.AppId, _spec.AppId, StringComparison.OrdinalIgnoreCase) ||
            package.App.InstallStrategy != InstallStrategy.SystemInstalled || package.App.DataStrategy != DataStrategy.NativeAccount)
            throw new InvalidOperationException($"This installer can only manage the trusted global {_spec.DisplayName} package.");
    }

    private sealed record MediaInstallation(string Executable, string Root, string Version);
}
