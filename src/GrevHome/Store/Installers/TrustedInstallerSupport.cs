using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace GrevHome.Store.Installers;

/// <summary>
/// Shared plumbing for the trusted per-app installer services (Steam, Discord, PCSX2, RetroArch).
/// Each installer still owns its own product-specific validation, staging layout and profile
/// configuration; this class holds only the download/verify/extract/cleanup mechanics that were
/// previously copy-pasted near-identically into every installer file.
/// </summary>
internal static class TrustedInstallerSupport
{
    public static HttpClient CreateHttpClient(string userAgent, TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    public static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left.Trim().Trim('"')),
                Path.GetFullPath(right.Trim().Trim('"')),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    public static string FormatBytes(long bytes)
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

    public static string RequireGrevId(PackageOperationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.GrevId))
        {
            throw new InvalidOperationException("A persistent Primary GrevID is required to manage this Profile App.");
        }

        return context.GrevId;
    }

    /// <summary>
    /// Downloads a single-file Windows installer (Steam/Discord shape: a direct HTTP GET with a
    /// fixed 0-68% progress band and a minimum-plausible-size check), reporting progress as it goes.
    /// </summary>
    public static async Task DownloadWindowsInstallerAsync(
        HttpClient http,
        Uri source,
        string destination,
        string downloadingLabel,
        string notPlausibleMessage,
        IProgress<PackageInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var length = response.Content.Headers.ContentLength;
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destination);
        var buffer = new byte[128 * 1024];
        long copied = 0;

        while (true)
        {
            var read = await responseStream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;

            if (length is > 0)
            {
                var downloadPercent = Math.Clamp(copied * 68d / length.Value, 0d, 68d);
                progress?.Report(new PackageInstallProgress(
                    "Download",
                    $"Downloading {downloadingLabel}… {copied / 1024d / 1024d:0.0} MB / {length.Value / 1024d / 1024d:0.0} MB",
                    downloadPercent));
            }
        }

        if (new FileInfo(destination).Length < 1024 * 1024)
        {
            throw new InvalidDataException(notPlausibleMessage);
        }
    }

    /// <summary>
    /// Downloads a package archive (PCSX2/RetroArch shape: routed through the central transfer
    /// queue when a <see cref="TrustedPackageDownloadService"/> is configured, otherwise a direct
    /// HTTP GET), reporting progress across the given <paramref name="progressStart"/>/
    /// <paramref name="progressEnd"/> band.
    /// </summary>
    public static async Task DownloadArchiveAsync(
        HttpClient http,
        TrustedPackageDownloadService? downloadService,
        string installerId,
        string displayName,
        Uri archiveUri,
        string downloadFileName,
        string grevId,
        string destination,
        IProgress<PackageInstallProgress>? progress,
        double progressStart,
        double progressEnd,
        string productName,
        CancellationToken cancellationToken)
    {
        if (downloadService is not null)
        {
            using var lease = await downloadService.DownloadAsync(
                installerId,
                displayName,
                archiveUri,
                downloadFileName,
                grevId,
                progress,
                progressStart,
                progressEnd,
                cancellationToken);
            File.Copy(lease.FilePath, destination, overwrite: false);
            if (new FileInfo(destination).Length <= 0)
            {
                throw new InvalidDataException($"{productName} download completed with no data.");
            }
            return;
        }

        using var response = await http.GetAsync(archiveUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
                var overallPercent = progressStart + (downloadPercent * (progressEnd - progressStart) / 100);
                progress?.Report(new PackageInstallProgress(
                    "Download",
                    $"{FormatBytes(received)} / {FormatBytes(total.Value)}",
                    overallPercent));
            }
        }

        await output.FlushAsync(cancellationToken);
        if (received <= 0)
        {
            throw new InvalidDataException($"{productName} download completed with no data.");
        }
    }

    public static async Task VerifySha256Async(
        string archivePath,
        string expectedHash,
        string productName,
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
            throw new InvalidDataException($"{productName} download failed SHA-256 verification. Nothing was installed.");
        }
    }

    public static async Task ValidateArchiveEntriesAsync(
        string archivePath,
        string productName,
        CancellationToken cancellationToken)
    {
        var result = await RunTarAsync(["-tf", archivePath], productName, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"Windows could not read the verified {productName} portable archive. " +
                TrimProcessError(result.StandardError));
        }

        var entries = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
        {
            throw new InvalidDataException($"The {productName} archive contains no files.");
        }

        foreach (var entry in entries)
        {
            var normalized = entry.Replace('\\', '/');
            if (normalized.StartsWith('/') ||
                normalized.Contains(':') ||
                normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == ".."))
            {
                throw new InvalidDataException($"{productName} archive contains an unsafe path. Nothing was extracted.");
            }
        }
    }

    public static async Task ExtractArchiveAsync(
        string archivePath,
        string extractRoot,
        string productName,
        CancellationToken cancellationToken)
    {
        var result = await RunTarAsync(["-xf", archivePath, "-C", extractRoot], productName, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"Windows could not extract the verified {productName} portable archive. " +
                TrimProcessError(result.StandardError));
        }
    }

    public static void MoveExtractedPackage(string extractedRoot, string targetRoot, string productName)
    {
        if (Directory.Exists(targetRoot))
        {
            if (Directory.EnumerateFileSystemEntries(targetRoot).Any())
            {
                throw new InvalidOperationException($"{productName} target folder stopped being empty during installation.");
            }

            Directory.Delete(targetRoot);
        }

        Directory.Move(extractedRoot, targetRoot);
    }

    private static async Task<TarResult> RunTarAsync(
        IReadOnlyList<string> arguments,
        string productName,
        CancellationToken cancellationToken)
    {
        var tarPath = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(tarPath))
        {
            throw new FileNotFoundException(
                $"Windows tar.exe is required for the controller-only {productName} portable installer.",
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

    private sealed record TarResult(int ExitCode, string StandardOutput, string StandardError);
}
