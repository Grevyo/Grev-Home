using System.IO;
using System.Text;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Runtime;

public sealed record LocalSessionHistoryEntry(
    int SchemaVersion,
    long Sequence,
    Guid SessionId,
    string AppId,
    string AppName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    long DurationSeconds,
    string Outcome,
    string? FailureMessage,
    string? ContentId = null,
    string? ContentName = null);

internal sealed record SessionHistorySequenceState(
    int SchemaVersion,
    long NextSequence);

/// <summary>
/// Durable, GrevID-owned history of completed managed-app sessions. This journal is local-first:
/// Grev Home writes it regardless of whether Grev.dad is linked or reachable. Online sync can
/// consume the immutable records later without becoming part of runtime/playtime success.
/// Optional ContentId/ContentName fields are reserved for future game/content launchers so the
/// history format does not need another redesign when an emulator session identifies a real game.
/// </summary>
public sealed class SessionHistoryService
{
    private const int SchemaVersion = 1;
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public SessionHistoryService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task RecordAsync(
        LaunchSessionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.EndedAtUtc is null || snapshot.State is not (LaunchSessionState.Exited or LaunchSessionState.Failed))
        {
            return;
        }

        var grevIds = snapshot.Participants
            .Select(participant => participant.GrevId)
            .Where(grevId => !string.IsNullOrWhiteSpace(grevId))
            .Select(grevId => grevId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (grevIds.Length == 0)
        {
            return;
        }

        var durationSeconds = Math.Max(
            0L,
            (long)Math.Round((snapshot.EndedAtUtc.Value - snapshot.StartedAtUtc).TotalSeconds));

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            foreach (var grevId in grevIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _paths.EnsureProfileLayout(grevId);

                var nextSequence = await ReadNextSequenceAsync(grevId, cancellationToken);
                var entry = new LocalSessionHistoryEntry(
                    SchemaVersion,
                    nextSequence,
                    snapshot.LaunchSessionId,
                    snapshot.AppId,
                    snapshot.AppName,
                    snapshot.StartedAtUtc,
                    snapshot.EndedAtUtc.Value,
                    durationSeconds,
                    snapshot.State == LaunchSessionState.Exited ? "exited" : "failed",
                    snapshot.FailureMessage);

                // Reserve the next sequence before appending. A crash can create a harmless gap,
                // but can never cause two different sessions to reuse one sequence number.
                await WriteSequenceStateAsync(
                    grevId,
                    new SessionHistorySequenceState(SchemaVersion, checked(nextSequence + 1)),
                    cancellationToken);
                await AppendEntryAsync(grevId, entry, cancellationToken);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<LocalSessionHistoryEntry>> ReadAfterAsync(
        string grevId,
        long afterSequence,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureProfileLayout(grevId);
        limit = Math.Clamp(limit, 1, 500);
        var entries = await ReadAllAsync(grevId, cancellationToken);
        return entries
            .Where(entry => entry.Sequence > Math.Max(0, afterSequence))
            .OrderBy(entry => entry.Sequence)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<LocalSessionHistoryEntry>> ReadRecentAsync(
        string grevId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureProfileLayout(grevId);
        limit = Math.Clamp(limit, 1, 500);
        var entries = await ReadAllAsync(grevId, cancellationToken);
        return entries
            .OrderByDescending(entry => entry.EndedAtUtc)
            .ThenByDescending(entry => entry.Sequence)
            .Take(limit)
            .ToArray();
    }

    private async Task<IReadOnlyList<LocalSessionHistoryEntry>> ReadAllAsync(
        string grevId,
        CancellationToken cancellationToken)
    {
        var path = GetHistoryFile(grevId);
        if (!File.Exists(path))
        {
            return Array.Empty<LocalSessionHistoryEntry>();
        }

        var entries = new List<LocalSessionHistoryEntry>();
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<LocalSessionHistoryEntry>(line, _json);
                if (entry is not null &&
                    entry.SchemaVersion == SchemaVersion &&
                    entry.Sequence > 0 &&
                    entry.SessionId != Guid.Empty &&
                    entry.EndedAtUtc >= entry.StartedAtUtc)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // One incomplete/corrupt append must not hide the rest of a local history journal.
            }
        }

        return entries;
    }

    private async Task<long> ReadNextSequenceAsync(string grevId, CancellationToken cancellationToken)
    {
        var statePath = GetSequenceStateFile(grevId);
        if (File.Exists(statePath))
        {
            try
            {
                await using var stream = File.OpenRead(statePath);
                var state = await JsonSerializer.DeserializeAsync<SessionHistorySequenceState>(stream, _json, cancellationToken);
                if (state is { SchemaVersion: SchemaVersion, NextSequence: > 0 })
                {
                    return state.NextSequence;
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        // Recovery path if the tiny sequence state file is lost/damaged: derive the next value
        // from the durable journal instead of resetting and reusing old sequence numbers.
        var existing = await ReadAllAsync(grevId, cancellationToken);
        return existing.Count == 0 ? 1 : checked(existing.Max(entry => entry.Sequence) + 1);
    }

    private async Task WriteSequenceStateAsync(
        string grevId,
        SessionHistorySequenceState state,
        CancellationToken cancellationToken)
    {
        var path = GetSequenceStateFile(grevId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, state, _json, cancellationToken);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task AppendEntryAsync(
        string grevId,
        LocalSessionHistoryEntry entry,
        CancellationToken cancellationToken)
    {
        var path = GetHistoryFile(grevId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(JsonSerializer.Serialize(entry, _json).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private string GetHistoryFile(string grevId) =>
        Path.Combine(_paths.GetProfileStats(grevId), "session-history.jsonl");

    private string GetSequenceStateFile(string grevId) =>
        Path.Combine(_paths.GetProfileStats(grevId), "session-history-sequence.json");
}
