using System.IO;
using GrevHome.Apps;
using GrevHome.Storage;
using GrevHome.Transfers;

namespace GrevHome.Store.Installers;

/// <summary>
/// Implemented by trusted package handlers whose Grev-owned downloads should flow through the
/// central transfer queue. Configuration is explicit and happens once during shell bootstrap.
/// </summary>
public interface ITrustedPackageDownloadConsumer
{
    void ConfigureDownloadService(TrustedPackageDownloadService downloadService);
}

/// <summary>
/// Bridges package-specific trusted installers to Grev Home's central persisted transfer queue.
/// The package still owns verification/extraction/installation; this service owns only the
/// observable download and returns the completed file for the trusted package workflow to inspect.
/// </summary>
public sealed class TrustedPackageDownloadService
{
    private static readonly TimeSpan ObservationInterval = TimeSpan.FromMilliseconds(150);

    private readonly AppPaths _paths;
    private readonly TransferManager _transfers;

    public TrustedPackageDownloadService(AppPaths paths, TransferManager transfers)
    {
        _paths = paths;
        _transfers = transfers;
    }

    public async Task<PackageDownloadLease> DownloadAsync(
        string appId,
        string displayName,
        Uri source,
        string fileName,
        string? ownerGrevId,
        IProgress<PackageInstallProgress>? progress,
        double progressStart,
        double progressEnd,
        CancellationToken cancellationToken = default)
    {
        appId = AppIdentity.ValidateAppId(appId);
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("A safe package download filename is required.", nameof(fileName));
        }

        progressStart = Math.Clamp(progressStart, 0d, 100d);
        progressEnd = Math.Clamp(progressEnd, progressStart, 100d);

        var operationId = Guid.NewGuid().ToString("N");
        var relativeDirectory = Path.Combine("Packages", appId, operationId);
        var relativeDestination = Path.Combine(relativeDirectory, fileName);
        var transfer = await _transfers.EnqueueDownloadAsync(
            source,
            relativeDestination,
            displayName,
            ownerGrevId,
            cancellationToken);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snapshot = await _transfers.GetSnapshotAsync(cancellationToken);
                var current = snapshot.Items.FirstOrDefault(item =>
                    string.Equals(item.Id, transfer.Id, StringComparison.OrdinalIgnoreCase));

                if (current is null)
                {
                    throw new InvalidOperationException("The package download disappeared from Grev Home's transfer queue.");
                }

                ReportProgress(current, displayName, progress, progressStart, progressEnd);

                switch (current.State)
                {
                    case TransferState.Completed:
                    {
                        var completedPath = ResolveCompletedPath(relativeDestination);
                        if (!File.Exists(completedPath))
                        {
                            throw new IOException("The transfer completed but its downloaded file is missing.");
                        }

                        return new PackageDownloadLease(completedPath, Path.GetDirectoryName(completedPath)!);
                    }
                    case TransferState.Failed:
                        throw new InvalidOperationException(
                            string.IsNullOrWhiteSpace(current.ErrorMessage)
                                ? "The package download failed."
                                : current.ErrorMessage);
                    case TransferState.Cancelled:
                        throw new InvalidOperationException("The package download was cancelled from Activity Center.");
                }

                await Task.Delay(ObservationInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            try
            {
                await _transfers.CancelAsync(transfer.Id, CancellationToken.None);
            }
            catch
            {
                // The caller's cancellation still wins even if transfer-state persistence fails.
            }
            throw;
        }
    }

    private string ResolveCompletedPath(string relativeDestination)
    {
        var downloadsRoot = Path.GetFullPath(_paths.Downloads);
        var completedPath = Path.GetFullPath(Path.Combine(downloadsRoot, relativeDestination));
        var prefix = downloadsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!completedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Package transfer path escaped the Grev Home Downloads root.");
        }

        return completedPath;
    }

    private static void ReportProgress(
        TransferItem item,
        string displayName,
        IProgress<PackageInstallProgress>? progress,
        double progressStart,
        double progressEnd)
    {
        if (progress is null)
        {
            return;
        }

        double? overall = null;
        string detail;
        if (item.TotalBytes is > 0)
        {
            var ratio = Math.Clamp((double)item.BytesReceived / item.TotalBytes.Value, 0d, 1d);
            overall = progressStart + ((progressEnd - progressStart) * ratio);
            detail = $"{displayName}: {FormatBytes(item.BytesReceived)} / {FormatBytes(item.TotalBytes.Value)}";
        }
        else if (item.BytesReceived > 0)
        {
            detail = $"{displayName}: {FormatBytes(item.BytesReceived)} received";
        }
        else
        {
            detail = item.State == TransferState.Queued
                ? $"{displayName} is queued in Activity Center."
                : $"Downloading {displayName}…";
        }

        progress.Report(new PackageInstallProgress("Download", detail, overall));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
        {
            return $"{bytes / (1024d * 1024d * 1024d):0.##} GB";
        }
        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / (1024d * 1024d):0.##} MB";
        }
        if (bytes >= 1024L)
        {
            return $"{bytes / 1024d:0.##} KB";
        }
        return $"{bytes} B";
    }
}

/// <summary>
/// Owns only Grev Home's temporary completed package-download directory. Disposing the lease does
/// not remove the Activity Center transfer-history entry and never touches installed app files.
/// </summary>
public sealed class PackageDownloadLease : IDisposable
{
    private readonly string _stagingDirectory;
    private bool _disposed;

    internal PackageDownloadLease(string filePath, string stagingDirectory)
    {
        FilePath = filePath;
        _stagingDirectory = stagingDirectory;
    }

    public string FilePath { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (Directory.Exists(_stagingDirectory))
            {
                Directory.Delete(_stagingDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Package cleanup is best-effort; transfer history remains valid either way.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
