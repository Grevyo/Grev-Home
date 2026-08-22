using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrevHome.Profiles;
using GrevHome.Runtime;
using GrevHome.Storage;

namespace GrevHome.Online;

public sealed record GrevDadSyncedProgression(
    long GrevHomeTotalXp,
    int GrevHomeLevel,
    long GrevDadTotalXp,
    int GrevDadLevel,
    DateTimeOffset SyncedAtUtc,
    bool HasMoreHistory);

internal sealed record GrevDadProfileSyncCursor(
    int SchemaVersion,
    long HistoryThroughSequence,
    DateTimeOffset? LastSuccessfulSyncAtUtc);

internal sealed record GrevDadSyncApiProgression(
    long TotalXp,
    int Level,
    long TotalTrackedSeconds,
    int CompletedSessions,
    int UniqueApps);

internal sealed record GrevDadSyncApiHomeResult(
    long TotalXp,
    int Level,
    long TotalTrackedSeconds,
    int CompletedSessions,
    int UniqueApps,
    long? UpdatedAt);

internal sealed record GrevDadSyncApiSiteResult(
    long TotalXp,
    int Level,
    long? UpdatedAt);

internal sealed record GrevDadSyncApiResponse(
    bool Ok,
    string? Message,
    int ApiVersion,
    long? AcceptedThroughSequence,
    GrevDadSyncApiHomeResult? GrevHome,
    GrevDadSyncApiSiteResult? GrevDad);

/// <summary>
/// Optional background bridge from local GrevID-owned data to Grev.dad. The source of truth stays
/// local: completed history is replayable and Grev Home progression is uploaded only as a snapshot.
/// GrevDadAccountService remains authoritative for link validity and revocation state; this worker
/// only transports local profile data after that account authority confirms the device link.
/// </summary>
public sealed class GrevDadProfileSyncService : IDisposable
{
    private const int SchemaVersion = 1;
    private const string AccessCredentialSlot = "access";
    private const int BatchSize = 100;
    private const int MaximumBatchesPerRun = 10;

    private readonly AppPaths _paths;
    private readonly SessionHistoryService _history;
    private readonly PlaytimeService _playtime;
    private readonly GrevDadAccountService _accounts;
    private readonly GrevDadPrivacySettingsService _privacy;
    private readonly WindowsCredentialSecretStore _secrets = new();
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _profileGates =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public GrevDadProfileSyncService(
        AppPaths paths,
        SessionHistoryService history,
        GrevDadAccountService accounts,
        GrevDadPrivacySettingsService privacy,
        Uri? baseUri = null)
    {
        _paths = paths;
        _history = history;
        _accounts = accounts;
        _privacy = privacy;
        _playtime = new PlaytimeService(paths);

        var configured = baseUri
            ?? TryReadConfiguredBaseUri()
            ?? new Uri("https://grev.dad/", UriKind.Absolute);
        if (!configured.IsAbsoluteUri || configured.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Grev.dad base URI must be absolute HTTPS.", nameof(baseUri));
        }

        _http = new HttpClient
        {
            BaseAddress = EnsureTrailingSlash(configured),
            Timeout = TimeSpan.FromSeconds(10)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GrevHome/Backbone-1");
    }

    public async Task<GrevDadSyncedProgression?> SyncAsync(
        string grevId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var gate = _profileGates.GetOrAdd(grevId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var account = await _accounts.ValidateLinkedAccountAsync(grevId, cancellationToken);
            if (account.State != GrevDadConnectionState.Linked)
            {
                return null;
            }

            var token = _secrets.Read(grevId, AccessCredentialSlot);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var cursor = await ReadCursorAsync(grevId, cancellationToken);
            var progression = await ReadStableLocalProgressionAsync(grevId, cancellationToken);
            var privacy = await _privacy.GetAsync(grevId, cancellationToken);
            GrevDadSyncApiResponse? lastResponse = null;
            var sentAnyRequest = false;

            for (var batchIndex = 0; batchIndex < MaximumBatchesPerRun; batchIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<LocalSessionHistoryEntry> sessions = privacy.ShareSessionHistory
                    ? await _history.ReadAfterAsync(
                        grevId,
                        cursor.HistoryThroughSequence,
                        BatchSize,
                        cancellationToken)
                    : Array.Empty<LocalSessionHistoryEntry>();

                if (sessions.Count == 0 && sentAnyRequest)
                {
                    break;
                }

                lastResponse = await SendBatchAsync(
                    grevId,
                    token,
                    progression,
                    sessions,
                    privacy.HistoryVisibility,
                    cancellationToken);
                sentAnyRequest = true;

                if (sessions.Count > 0)
                {
                    var expectedThrough = sessions.Max(session => session.Sequence);
                    if (lastResponse.AcceptedThroughSequence is null ||
                        lastResponse.AcceptedThroughSequence.Value < expectedThrough)
                    {
                        throw new InvalidDataException("Grev.dad did not acknowledge the complete Grev Home history batch.");
                    }

                    cursor = cursor with
                    {
                        HistoryThroughSequence = Math.Max(
                            cursor.HistoryThroughSequence,
                            lastResponse.AcceptedThroughSequence.Value),
                        LastSuccessfulSyncAtUtc = DateTimeOffset.UtcNow
                    };
                    await WriteCursorAsync(grevId, cursor, cancellationToken);
                }
                else
                {
                    cursor = cursor with { LastSuccessfulSyncAtUtc = DateTimeOffset.UtcNow };
                    await WriteCursorAsync(grevId, cursor, cancellationToken);
                    break;
                }

                if (sessions.Count < BatchSize)
                {
                    break;
                }
            }

            if (lastResponse?.GrevHome is null || lastResponse.GrevDad is null)
            {
                return null;
            }

            var hasMoreHistory = privacy.ShareSessionHistory &&
                                 (await _history.ReadAfterAsync(
                                     grevId,
                                     cursor.HistoryThroughSequence,
                                     1,
                                     cancellationToken)).Count > 0;

            return new GrevDadSyncedProgression(
                lastResponse.GrevHome.TotalXp,
                lastResponse.GrevHome.Level,
                lastResponse.GrevDad.TotalXp,
                lastResponse.GrevDad.Level,
                DateTimeOffset.UtcNow,
                hasMoreHistory);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<GrevDadSyncApiProgression> ReadStableLocalProgressionAsync(
        string grevId,
        CancellationToken cancellationToken)
    {
        var playtime = await _playtime.GetForGrevIdAsync(grevId, cancellationToken);
        var totalSeconds = playtime.Apps.Values.Sum(app => app.TotalSeconds);
        var completedSessions = playtime.Apps.Values.Sum(app => app.SessionCount);
        var uniqueApps = playtime.Apps.Count;

        // One progression authority is shared by the local profile UI and optional Grev.dad sync.
        // Sync intentionally uses committed playtime only, not temporary in-flight runtime seconds.
        var totalXp = GrevHomeProgressionPolicy.CalculateXp(
            totalSeconds,
            completedSessions,
            uniqueApps);
        var level = GrevHomeProgressionPolicy.CalculateLevel(totalXp).Level;
        return new GrevDadSyncApiProgression(
            totalXp,
            level,
            totalSeconds,
            completedSessions,
            uniqueApps);
    }

    private async Task<GrevDadSyncApiResponse> SendBatchAsync(
        string grevId,
        string token,
        GrevDadSyncApiProgression progression,
        IReadOnlyList<LocalSessionHistoryEntry> sessions,
        string historyVisibility,
        CancellationToken cancellationToken)
    {
        var visibility = string.Equals(historyVisibility, "private", StringComparison.OrdinalIgnoreCase)
            ? "private"
            : "friends";
        var body = new
        {
            progression,
            sessions = sessions.Select(session => new
            {
                sessionId = session.SessionId,
                sequence = session.Sequence,
                appId = session.AppId,
                appName = session.AppName,
                contentId = session.ContentId,
                contentName = session.ContentName,
                startedAt = session.StartedAtUtc.ToUnixTimeSeconds(),
                endedAt = session.EndedAtUtc.ToUnixTimeSeconds(),
                durationSeconds = session.DurationSeconds,
                outcome = session.Outcome,
                failureMessage = session.FailureMessage,
                visibility
            }).ToArray()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/grev-home/sync")
        {
            Content = JsonContent.Create(body, options: _json)
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            try
            {
                await _accounts.ValidateLinkedAccountAsync(grevId, cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException or InvalidDataException)
            {
            }

            throw new InvalidOperationException("The Grev.dad device link is no longer authorised.");
        }

        GrevDadSyncApiResponse payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<GrevDadSyncApiResponse>(_json, cancellationToken)
                      ?? throw new InvalidDataException("Grev.dad returned an empty sync response.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Grev.dad returned an incompatible sync response.", ex);
        }

        if (!response.IsSuccessStatusCode || !payload.Ok)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(payload.Message)
                    ? $"Grev.dad sync failed with HTTP {(int)response.StatusCode}."
                    : payload.Message);
        }
        if (payload.ApiVersion != GrevDadAccountService.SupportedApiVersion)
        {
            throw new InvalidDataException(
                $"Grev.dad sync API {payload.ApiVersion} is not compatible with Grev Home API {GrevDadAccountService.SupportedApiVersion}.");
        }

        return payload;
    }

    private async Task<GrevDadProfileSyncCursor> ReadCursorAsync(
        string grevId,
        CancellationToken cancellationToken)
    {
        var path = GetCursorFile(grevId);
        if (!File.Exists(path))
        {
            return new GrevDadProfileSyncCursor(SchemaVersion, 0, null);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var cursor = await JsonSerializer.DeserializeAsync<GrevDadProfileSyncCursor>(stream, _json, cancellationToken);
            if (cursor is { SchemaVersion: SchemaVersion, HistoryThroughSequence: >= 0 })
            {
                return cursor;
            }

            CorruptDataQuarantine.TryPreserve(
                _paths,
                path,
                "GrevDadSyncCursor",
                "Sync cursor is empty or uses an unsupported schema/value.",
                out _);
            return new GrevDadProfileSyncCursor(SchemaVersion, 0, null);
        }
        catch (JsonException ex)
        {
            CorruptDataQuarantine.TryPreserve(
                _paths,
                path,
                "GrevDadSyncCursor",
                $"Sync cursor JSON could not be parsed: {ex.Message}",
                out _);
            return new GrevDadProfileSyncCursor(SchemaVersion, 0, null);
        }
        catch (IOException)
        {
            return new GrevDadProfileSyncCursor(SchemaVersion, 0, null);
        }
    }

    private async Task WriteCursorAsync(
        string grevId,
        GrevDadProfileSyncCursor cursor,
        CancellationToken cancellationToken)
    {
        var path = GetCursorFile(grevId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, cursor, _json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string GetCursorFile(string grevId) =>
        Path.Combine(_paths.GetProfileConnections(grevId), "GrevDad", "sync.json");

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        return new Uri(value, UriKind.Absolute);
    }

    private static Uri? TryReadConfiguredBaseUri()
    {
        var value = Environment.GetEnvironmentVariable("GREV_DAD_BASE_URI");
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
        foreach (var gate in _profileGates.Values)
        {
            gate.Dispose();
        }
        _profileGates.Clear();
    }
}
