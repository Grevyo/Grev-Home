using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrevHome.Storage;

namespace GrevHome.Updates;

public sealed record GrevHomeUpdate(string Version, Uri Installer, Uri Checksum);

public sealed class GrevHomeUpdateService
{
    private static readonly Uri LatestRelease = new("https://api.github.com/repos/Grevyo/Grev-Home/releases/latest");
    private readonly AppPaths _paths;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };

    public GrevHomeUpdateService(AppPaths paths)
    {
        _paths = paths;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GrevHome-Updater/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<GrevHomeUpdate?> CheckAsync(CancellationToken token = default)
    {
        using var response = await _http.GetAsync(LatestRelease, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (document.RootElement.GetProperty("draft").GetBoolean() || document.RootElement.GetProperty("prerelease").GetBoolean()) return null;
        var tag = document.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var available)) return null;
        available = new Version(available.Major, available.Minor, Math.Max(0, available.Build), Math.Max(0, available.Revision));
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        if (available <= current) return null;
        Uri? installer = null, checksum = null;
        foreach (var asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsReleaseAsset(uri)) continue;
            if (string.Equals(name, "GrevHomeSetup.exe", StringComparison.OrdinalIgnoreCase)) installer = uri;
            if (string.Equals(name, "GrevHomeSetup.sha256.txt", StringComparison.OrdinalIgnoreCase)) checksum = uri;
        }
        return installer is not null && checksum is not null ? new GrevHomeUpdate(available.ToString(), installer, checksum) : null;
    }

    public async Task DownloadAndLaunchAsync(GrevHomeUpdate update, CancellationToken token = default)
    {
        if (!Version.TryParse(update.Version, out _) || !IsReleaseAsset(update.Installer) || !IsReleaseAsset(update.Checksum))
            throw new InvalidDataException("The update does not identify an official Grev Home release.");
        var updateRoot = Path.Combine(_paths.Root, "Updates", update.Version);
        Directory.CreateDirectory(updateRoot);
        var installerPath = Path.Combine(updateRoot, "GrevHomeSetup.exe");
        var temporaryPath = installerPath + ".download";
        var checksumText = await _http.GetStringAsync(update.Checksum, token);
        var expected = Regex.Match(checksumText, "[A-Fa-f0-9]{64}").Value;
        if (expected.Length != 64) throw new InvalidDataException("The release checksum was missing or invalid.");
        using (var response = await _http.GetAsync(update.Installer, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(token);
            await using var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await source.CopyToAsync(destination, token);
        }
        await using (var source = File.OpenRead(temporaryPath))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(source, token));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded installer did not match the release checksum.");
        }
        File.Move(temporaryPath, installerPath, true);
        using var process = Process.Start(new ProcessStartInfo(installerPath, "/SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS") { UseShellExecute = true });
        if (process is null) throw new IOException("Windows could not start the installer.");
    }

    public static bool IsReleaseAsset(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath.StartsWith("/Grevyo/Grev-Home/releases/download/", StringComparison.Ordinal)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
