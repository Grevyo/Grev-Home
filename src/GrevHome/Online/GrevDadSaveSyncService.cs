using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Online;

public enum CloudSaveStatus
{
    NotLinked,
    Disabled,
    NeverSynced,
    UpToDate,
    LocalChangesPending,
    RemoteChangesAvailable,
    Conflict,
    Syncing,
    Offline,
    Error
}

public sealed record CloudSaveState(
    CloudSaveStatus Status,
    DateTimeOffset? LastUploadedAtUtc,
    DateTimeOffset? LastDownloadedAtUtc,
    string? Message);

internal sealed record CloudSaveManifest(
    int SchemaVersion,
    bool Enabled,
    string? LastUploadedHash,
    DateTimeOffset? LastUploadedAtUtc,
    DateTimeOffset? LastDownloadedAtUtc,
    DateTimeOffset? LastKnownRemoteUpdatedAtUtc = null)
{
    public static CloudSaveManifest Empty { get; } = new(1, false, null, null, null, null);
}

internal sealed record GrevDadSaveApiResponse(bool Ok, string? Message, int ApiVersion, bool Exists, long? SizeBytes, DateTimeOffset? UpdatedAtUtc);

/// <summary>
/// Optional background bridge for one GrevID-owned app's save data, matching the same
/// contract GrevDadProfileSyncService already establishes for progression: the source of truth
/// stays local (the app's folder under <c>Saves/&lt;AppId&gt;</c>), GrevDadAccountService remains
/// authoritative for link validity, and this service only transports that local data after the
/// account authority confirms the device link. Cloud saves are opt-in per app per GrevID - never
/// on by default, never required for local play, and a network failure here can never block or
/// interrupt launching or playing the app itself.
/// </summary>
public sealed class GrevDadSaveSyncService : IDisposable
{
    private const int SchemaVersion = 1;
    private const string AccessCredentialSlot = "access";
    // Sent by Grev.dad on both the upload response body (GrevDadSaveApiResponse.UpdatedAtUtc) and,
    // since a save GET's body is the archive itself, as a response header on GET/HEAD so a conflict
    // check can learn the remote's timestamp without downloading the whole archive.
    private const string RemoteUpdatedAtHeader = "X-Grev-Updated-At";
    // A generous but finite ceiling: this guards against archiving a save folder someone pointed
    // at something enormous by mistake (a whole emulator BIOS/ROM tree, say), not a real save.
    private const long MaxArchiveBytes = 300L * 1024 * 1024;

    private readonly AppPaths _paths;
    private readonly GrevDadAccountService _accounts;
    private readonly WindowsCredentialSecretStore _secrets = new();
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = JsonDefaults.IndentedWeb;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public GrevDadSaveSyncService(AppPaths paths, GrevDadAccountService accounts, Uri? baseUri = null)
    {
        _paths = paths;
        _accounts = accounts;
        // Save archives are larger and slower than the JSON calls the other Grev.dad services
        // make, so this gets its own longer timeout rather than sharing one tuned for small payloads.
        _http = GrevDadNetworkSupport.CreateHttpClient(
            baseUri,
            new Uri("https://grev.dad/", UriKind.Absolute),
            TimeSpan.FromMinutes(3));
    }

    public async Task<bool> IsEnabledAsync(string grevId, string appId, CancellationToken cancellationToken = default) =>
        (await ReadManifestAsync(grevId, appId, cancellationToken)).Enabled;

    public async Task SetEnabledAsync(string grevId, string appId, bool enabled, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var gate = GetGate(grevId, appId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await ReadManifestAsync(grevId, appId, cancellationToken);
            await WriteManifestAsync(grevId, appId, manifest with { Enabled = enabled }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The status shown in Settings, computed entirely from local state plus the account service's
    /// already-cached link snapshot - this never makes a network call by itself, so opening app
    /// settings is never slowed down or blocked by Grev.dad being unreachable.
    /// </summary>
    public async Task<CloudSaveState> GetStatusAsync(string grevId, string appId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var manifest = await ReadManifestAsync(grevId, appId, cancellationToken);
        if (!manifest.Enabled)
        {
            return new CloudSaveState(CloudSaveStatus.Disabled, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, null);
        }

        if (_accounts.GetLastSnapshot(grevId).State != GrevDadConnectionState.Linked)
        {
            return new CloudSaveState(
                CloudSaveStatus.NotLinked,
                manifest.LastUploadedAtUtc,
                manifest.LastDownloadedAtUtc,
                "Link this GrevID to Grev.dad in Profile to use cloud saves.");
        }

        if (manifest.LastUploadedHash is null)
        {
            return new CloudSaveState(CloudSaveStatus.NeverSynced, null, manifest.LastDownloadedAtUtc, null);
        }

        try
        {
            var currentHash = ComputeLocalHash(_paths.GetProfileAppSaves(grevId, appId));
            var status = string.Equals(currentHash, manifest.LastUploadedHash, StringComparison.Ordinal)
                ? CloudSaveStatus.UpToDate
                : CloudSaveStatus.LocalChangesPending;
            return new CloudSaveState(status, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CloudSaveState(CloudSaveStatus.Error, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, ex.Message);
        }
    }

    /// <summary>
    /// Archives the local save folder and uploads it. Returns the resulting state rather than
    /// throwing for every expected outcome (disabled, unlinked, offline, empty folder); callers
    /// that want to know whether an *unexpected* failure happened should check
    /// <see cref="CloudSaveStatus.Error"/> specifically.
    /// </summary>
    public async Task<CloudSaveState> UploadAsync(string grevId, string appId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var gate = GetGate(grevId, appId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await ReadManifestAsync(grevId, appId, cancellationToken);
            if (!manifest.Enabled)
            {
                return new CloudSaveState(CloudSaveStatus.Disabled, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, null);
            }

            var account = await _accounts.ValidateLinkedAccountAsync(grevId, cancellationToken);
            if (account.State != GrevDadConnectionState.Linked)
            {
                return new CloudSaveState(
                    account.State == GrevDadConnectionState.Offline ? CloudSaveStatus.Offline : CloudSaveStatus.NotLinked,
                    manifest.LastUploadedAtUtc,
                    manifest.LastDownloadedAtUtc,
                    account.Message);
            }

            var token = _secrets.Read(grevId, AccessCredentialSlot);
            if (string.IsNullOrWhiteSpace(token))
            {
                return new CloudSaveState(CloudSaveStatus.NotLinked, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, null);
            }

            var saveRoot = _paths.GetProfileAppSaves(grevId, appId);
            if (!Directory.Exists(saveRoot) || !Directory.EnumerateFileSystemEntries(saveRoot).Any())
            {
                return new CloudSaveState(CloudSaveStatus.NeverSynced, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, "There is no local save data for this app yet.");
            }

            var hash = ComputeLocalHash(saveRoot);
            var archivePath = Path.Combine(Path.GetTempPath(), $"GrevHomeSave-{Guid.NewGuid():N}.zip");
            DateTimeOffset? remoteUpdatedAt = null;
            try
            {
                ZipFile.CreateFromDirectory(saveRoot, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
                var archiveInfo = new FileInfo(archivePath);
                if (archiveInfo.Length > MaxArchiveBytes)
                {
                    return new CloudSaveState(
                        CloudSaveStatus.Error,
                        manifest.LastUploadedAtUtc,
                        manifest.LastDownloadedAtUtc,
                        $"This save folder is {archiveInfo.Length / (1024 * 1024):N0} MB, over the {MaxArchiveBytes / (1024 * 1024):N0} MB cloud save limit.");
                }

                using var request = new HttpRequestMessage(HttpMethod.Put, $"api/grev-home/saves/{appId}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                await using (var archiveStream = File.OpenRead(archivePath))
                {
                    using var content = new StreamContent(archiveStream);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                    request.Content = content;
                    using var response = await _http.SendAsync(request, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        return new CloudSaveState(
                            CloudSaveStatus.Error,
                            manifest.LastUploadedAtUtc,
                            manifest.LastDownloadedAtUtc,
                            $"Grev.dad rejected the upload ({(int)response.StatusCode}).");
                    }

                    var result = await GrevDadNetworkSupport.ReadJsonAsync<GrevDadSaveApiResponse>(response, _json, cancellationToken);
                    EnsureApiVersion(result.ApiVersion);
                    remoteUpdatedAt = result.UpdatedAtUtc;
                }
            }
            finally
            {
                if (File.Exists(archivePath)) File.Delete(archivePath);
            }

            var uploadedAt = DateTimeOffset.UtcNow;
            await WriteManifestAsync(
                grevId,
                appId,
                manifest with
                {
                    LastUploadedHash = hash,
                    LastUploadedAtUtc = uploadedAt,
                    LastKnownRemoteUpdatedAtUtc = remoteUpdatedAt ?? uploadedAt
                },
                cancellationToken);
            return new CloudSaveState(CloudSaveStatus.UpToDate, uploadedAt, manifest.LastDownloadedAtUtc, null);
        }
        catch (Exception ex) when (IsNetworkFailure(ex))
        {
            var manifest = await ReadManifestAsync(grevId, appId, cancellationToken);
            return new CloudSaveState(CloudSaveStatus.Offline, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, "Grev.dad could not be reached.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var manifest = await ReadManifestAsync(grevId, appId, cancellationToken);
            return new CloudSaveState(CloudSaveStatus.Error, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Downloads the cloud save and replaces the local one. The existing local save folder is
    /// never overwritten in place: it is moved aside to a timestamped backup first, and the
    /// downloaded archive is fully extracted to a staging directory and validated before anything
    /// replaces live data, so a truncated download or a mid-extract failure can never leave a
    /// half-written save folder behind.
    /// </summary>
    public async Task<CloudSaveState> DownloadAsync(string grevId, string appId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var gate = GetGate(grevId, appId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await ReadManifestAsync(grevId, appId, cancellationToken);
            if (!manifest.Enabled)
            {
                return new CloudSaveState(CloudSaveStatus.Disabled, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, null);
            }

            var account = await _accounts.ValidateLinkedAccountAsync(grevId, cancellationToken);
            if (account.State != GrevDadConnectionState.Linked)
            {
                return new CloudSaveState(
                    account.State == GrevDadConnectionState.Offline ? CloudSaveStatus.Offline : CloudSaveStatus.NotLinked,
                    manifest.LastUploadedAtUtc,
                    manifest.LastDownloadedAtUtc,
                    account.Message);
            }

            var token = _secrets.Read(grevId, AccessCredentialSlot);
            if (string.IsNullOrWhiteSpace(token))
            {
                return new CloudSaveState(CloudSaveStatus.NotLinked, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, null);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/grev-home/saves/{appId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new CloudSaveState(CloudSaveStatus.NeverSynced, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, "No cloud save has been uploaded for this app yet.");
            }
            if (!response.IsSuccessStatusCode)
            {
                return new CloudSaveState(
                    CloudSaveStatus.Error,
                    manifest.LastUploadedAtUtc,
                    manifest.LastDownloadedAtUtc,
                    $"Grev.dad could not return the cloud save ({(int)response.StatusCode}).");
            }

            var archivePath = Path.Combine(Path.GetTempPath(), $"GrevHomeSave-{Guid.NewGuid():N}.zip");
            // Staged as a sibling of the save folder itself, on the same drive it will replace,
            // rather than in the system temp directory: Directory.Move (used below to swap it and
            // the previous save into place) fails across volumes on Windows, and Grev Home's root
            // is not guaranteed to be on the same drive as %TEMP%.
            var savesRoot = _paths.GetProfileSaves(grevId);
            var stagingRoot = Path.Combine(savesRoot, $".staging-{appId}-{Guid.NewGuid():N}");
            try
            {
                await using (var archiveFile = File.Create(archivePath))
                {
                    await response.Content.CopyToAsync(archiveFile, cancellationToken);
                }

                Directory.CreateDirectory(stagingRoot);
                ZipFile.ExtractToDirectory(archivePath, stagingRoot, overwriteFiles: true);

                var saveRoot = _paths.GetProfileAppSaves(grevId, appId);
                Directory.CreateDirectory(savesRoot);
                if (Directory.Exists(saveRoot) && Directory.EnumerateFileSystemEntries(saveRoot).Any())
                {
                    var backupRoot = $"{saveRoot}.backup-{DateTime.Now:yyyyMMdd-HHmmss}";
                    Directory.Move(saveRoot, backupRoot);
                }
                Directory.Move(stagingRoot, saveRoot);
            }
            finally
            {
                if (File.Exists(archivePath)) File.Delete(archivePath);
                if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
            }

            var downloadedAt = DateTimeOffset.UtcNow;
            var hash = ComputeLocalHash(_paths.GetProfileAppSaves(grevId, appId));
            var remoteUpdatedAt = TryReadRemoteUpdatedAtHeader(response);
            await WriteManifestAsync(
                grevId,
                appId,
                manifest with
                {
                    LastUploadedHash = hash,
                    LastDownloadedAtUtc = downloadedAt,
                    LastKnownRemoteUpdatedAtUtc = remoteUpdatedAt ?? downloadedAt
                },
                cancellationToken);
            return new CloudSaveState(CloudSaveStatus.UpToDate, manifest.LastUploadedAtUtc, downloadedAt, null);
        }
        catch (Exception ex) when (IsNetworkFailure(ex))
        {
            var manifest = await ReadManifestAsync(grevId, appId, cancellationToken);
            return new CloudSaveState(CloudSaveStatus.Offline, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, "Grev.dad could not be reached.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var manifest = await ReadManifestAsync(grevId, appId, cancellationToken);
            return new CloudSaveState(CloudSaveStatus.Error, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Every app under this GrevID that currently has cloud saves turned on. Manifests are kept as
    /// one file per app with no separate index, so this lists the per-GrevID manifest directory and
    /// reads each one; a login-time conflict sweep is the only caller and already tolerates the
    /// small extra I/O this costs over an index it would otherwise have to keep in sync.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetEnabledAppsAsync(string grevId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var directory = Path.Combine(_paths.GetProfileConnections(grevId), "GrevDad", "cloud-saves");
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var enabled = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var appId = Path.GetFileNameWithoutExtension(file);
            if (await IsEnabledAsync(grevId, appId, cancellationToken))
            {
                enabled.Add(appId);
            }
        }

        return enabled;
    }

    /// <summary>
    /// A lightweight (no archive download) network check for whether local and/or remote save data
    /// has changed since Grev Home last touched this app's cloud save, so a real conflict (both
    /// sides changed independently) can be told apart from an ordinary one-sided change. Unlike
    /// <see cref="GetStatusAsync"/> this does make a network call - callers (login sync, the
    /// pre-upload check after a session ends) treat it as best-effort and fall back to the local-only
    /// status on failure rather than blocking on it.
    /// </summary>
    public async Task<CloudSaveState> CheckRemoteAsync(string grevId, string appId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var manifest = await ReadManifestAsync(grevId, appId, cancellationToken);
        if (!manifest.Enabled)
        {
            return new CloudSaveState(CloudSaveStatus.Disabled, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, null);
        }

        if (_accounts.GetLastSnapshot(grevId).State != GrevDadConnectionState.Linked)
        {
            return new CloudSaveState(
                CloudSaveStatus.NotLinked,
                manifest.LastUploadedAtUtc,
                manifest.LastDownloadedAtUtc,
                "Link this GrevID to Grev.dad in Profile to use cloud saves.");
        }

        if (manifest.LastUploadedHash is null)
        {
            return new CloudSaveState(CloudSaveStatus.NeverSynced, null, manifest.LastDownloadedAtUtc, null);
        }

        var token = _secrets.Read(grevId, AccessCredentialSlot);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new CloudSaveState(CloudSaveStatus.NotLinked, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, null);
        }

        try
        {
            string currentHash;
            try
            {
                currentHash = ComputeLocalHash(_paths.GetProfileAppSaves(grevId, appId));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new CloudSaveState(CloudSaveStatus.Error, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, ex.Message);
            }
            var localChanged = !string.Equals(currentHash, manifest.LastUploadedHash, StringComparison.Ordinal);

            using var request = new HttpRequestMessage(HttpMethod.Head, $"api/grev-home/saves/{appId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new CloudSaveState(
                    localChanged ? CloudSaveStatus.LocalChangesPending : CloudSaveStatus.UpToDate,
                    manifest.LastUploadedAtUtc,
                    manifest.LastDownloadedAtUtc,
                    null);
            }
            if (!response.IsSuccessStatusCode)
            {
                return new CloudSaveState(
                    localChanged ? CloudSaveStatus.LocalChangesPending : CloudSaveStatus.UpToDate,
                    manifest.LastUploadedAtUtc,
                    manifest.LastDownloadedAtUtc,
                    $"Could not check for remote changes ({(int)response.StatusCode}). Showing local state only.");
            }

            var remoteUpdatedAt = TryReadRemoteUpdatedAtHeader(response);
            var remoteChanged = remoteUpdatedAt is not null &&
                                 manifest.LastKnownRemoteUpdatedAtUtc is not null &&
                                 remoteUpdatedAt.Value > manifest.LastKnownRemoteUpdatedAtUtc.Value;

            var status = (localChanged, remoteChanged) switch
            {
                (true, true) => CloudSaveStatus.Conflict,
                (false, true) => CloudSaveStatus.RemoteChangesAvailable,
                (true, false) => CloudSaveStatus.LocalChangesPending,
                _ => CloudSaveStatus.UpToDate
            };

            return new CloudSaveState(status, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, null);
        }
        catch (Exception ex) when (IsNetworkFailure(ex))
        {
            return new CloudSaveState(CloudSaveStatus.Offline, manifest.LastUploadedAtUtc, manifest.LastDownloadedAtUtc, "Grev.dad could not be reached.");
        }
    }

    /// <summary>
    /// Resolves a detected conflict by explicit user choice: <paramref name="keepLocal"/> pushes the
    /// local save over the remote one (an ordinary upload); otherwise the remote save replaces the
    /// local one (an ordinary download, with the same non-destructive backup-before-replace safety).
    /// There is no automatic merge - cloud saves are transport only and never inspect save content.
    /// </summary>
    public Task<CloudSaveState> ResolveConflictAsync(string grevId, string appId, bool keepLocal, CancellationToken cancellationToken = default) =>
        keepLocal ? UploadAsync(grevId, appId, cancellationToken) : DownloadAsync(grevId, appId, cancellationToken);

    private static DateTimeOffset? TryReadRemoteUpdatedAtHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues(RemoteUpdatedAtHeader, out var values) &&
        DateTimeOffset.TryParse(values.FirstOrDefault(), out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// A stable, order-independent hash of a save folder's exact contents (relative path and
    /// bytes of every file). Used only to answer "has anything local changed since the last
    /// upload/download" without a network round trip - it is never sent anywhere.
    /// </summary>
    public static string ComputeLocalHash(string root)
    {
        using var sha = SHA256.Create();
        if (!Directory.Exists(root)) return Convert.ToHexString(sha.ComputeHash([]));

        using var combined = new MemoryStream();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var relativeBytes = System.Text.Encoding.UTF8.GetBytes(relative);
            combined.Write(relativeBytes, 0, relativeBytes.Length);
            using var fileStream = File.OpenRead(file);
            fileStream.CopyTo(combined);
        }

        combined.Position = 0;
        return Convert.ToHexString(sha.ComputeHash(combined));
    }

    private SemaphoreSlim GetGate(string grevId, string appId) =>
        _gates.GetOrAdd($"{grevId}::{appId}", static _ => new SemaphoreSlim(1, 1));

    private async Task<CloudSaveManifest> ReadManifestAsync(string grevId, string appId, CancellationToken cancellationToken)
    {
        var path = GetManifestFile(grevId, appId);
        if (!File.Exists(path)) return CloudSaveManifest.Empty;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<CloudSaveManifest>(stream, _json, cancellationToken) ?? CloudSaveManifest.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return CloudSaveManifest.Empty;
        }
    }

    private async Task WriteManifestAsync(string grevId, string appId, CloudSaveManifest manifest, CancellationToken cancellationToken) =>
        await GrevDadNetworkSupport.WriteJsonAtomicallyAsync(GetManifestFile(grevId, appId), manifest, _json, cancellationToken);

    private string GetManifestFile(string grevId, string appId) =>
        Path.Combine(_paths.GetProfileConnections(grevId), "GrevDad", "cloud-saves", $"{appId}.json");

    private static bool IsNetworkFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or TimeoutException;

    private void EnsureApiVersion(int version)
    {
        if (version != GrevDadAccountService.SupportedApiVersion)
        {
            throw new InvalidDataException(
                $"Grev.dad cloud save API {version} is not compatible with this Grev Home build (API {GrevDadAccountService.SupportedApiVersion}).");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
