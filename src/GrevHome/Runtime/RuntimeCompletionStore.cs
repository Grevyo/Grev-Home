using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Runtime;

public sealed record RuntimePendingCompletionRecord(
    int SchemaVersion,
    Guid LaunchSessionId,
    string AppId,
    string AppName,
    string? PrimaryGrevId,
    IReadOnlyList<LaunchParticipant> Participants,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    LaunchSessionState State,
    int RootProcessId,
    IReadOnlyList<int> ProcessIds,
    string? FailureMessage)
{
    public LaunchSessionSnapshot ToSnapshot() => new(
        LaunchSessionId,
        AppId,
        AppName,
        PrimaryGrevId,
        Participants,
        StartedAtUtc,
        EndedAtUtc,
        State,
        RootProcessId,
        ProcessIds,
        FailureMessage);
}

/// <summary>
/// Crash-safe handoff between process-exit detection and local completion persistence. A pending
/// record is written before playtime/history are touched and is deleted only after both idempotent
/// stores commit. Hard termination at any point therefore leaves enough exact data to replay the
/// completion without guessing an end time or double-awarding playtime/XP.
/// </summary>
public sealed class RuntimeCompletionStore
{
    private const int SchemaVersion = 1;

    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public RuntimeCompletionStore(AppPaths paths)
    {
        _paths = paths;
    }

    public string Root => Path.Combine(_paths.RuntimeData, "PendingCompletions");

    public async Task SaveAsync(
        LaunchSessionSnapshot runningSnapshot,
        DateTimeOffset endedAtUtc,
        string? failureMessage,
        CancellationToken cancellationToken = default)
    {
        if (runningSnapshot.LaunchSessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(runningSnapshot.AppId) ||
            string.IsNullOrWhiteSpace(runningSnapshot.AppName) ||
            endedAtUtc < runningSnapshot.StartedAtUtc)
        {
            throw new InvalidDataException("Runtime completion data is invalid.");
        }

        var state = failureMessage is null
            ? LaunchSessionState.Exited
            : LaunchSessionState.Failed;
        var record = new RuntimePendingCompletionRecord(
            SchemaVersion,
            runningSnapshot.LaunchSessionId,
            runningSnapshot.AppId,
            runningSnapshot.AppName,
            runningSnapshot.PrimaryGrevId,
            runningSnapshot.Participants ?? Array.Empty<LaunchParticipant>(),
            runningSnapshot.StartedAtUtc,
            endedAtUtc,
            state,
            runningSnapshot.RootProcessId,
            runningSnapshot.ProcessIds ?? Array.Empty<int>(),
            failureMessage);

        await SaveAsync(record, cancellationToken);
    }

    public async Task SaveAsync(
        RuntimePendingCompletionRecord record,
        CancellationToken cancellationToken = default)
    {
        Validate(record);
        Directory.CreateDirectory(Root);
        var path = GetPath(record.LaunchSessionId);
        var temporary = path + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, _json, cancellationToken);
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

    public IReadOnlyList<RuntimePendingCompletionRecord> LoadAll()
    {
        if (!Directory.Exists(Root))
        {
            return Array.Empty<RuntimePendingCompletionRecord>();
        }

        var records = new List<RuntimePendingCompletionRecord>();
        foreach (var path in Directory.EnumerateFiles(Root, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = File.OpenRead(path);
                var record = JsonSerializer.Deserialize<RuntimePendingCompletionRecord>(stream, _json);
                if (record is null)
                {
                    PreserveMalformed(path, "Pending runtime completion was empty.");
                    continue;
                }

                Validate(record);
                records.Add(record);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException)
            {
                PreserveMalformed(path, $"Pending runtime completion could not be read: {ex.Message}");
            }
            catch (IOException)
            {
                // A temporarily locked completion remains in place for the next replay attempt.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return records
            .OrderBy(record => record.EndedAtUtc)
            .ThenBy(record => record.LaunchSessionId)
            .ToArray();
    }

    public void Delete(Guid launchSessionId)
    {
        var path = GetPath(launchSessionId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void Validate(RuntimePendingCompletionRecord record)
    {
        if (record.SchemaVersion != SchemaVersion ||
            record.LaunchSessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(record.AppId) ||
            string.IsNullOrWhiteSpace(record.AppName) ||
            record.StartedAtUtc == default ||
            record.EndedAtUtc < record.StartedAtUtc ||
            record.State is not (LaunchSessionState.Exited or LaunchSessionState.Failed))
        {
            throw new InvalidDataException("Pending runtime completion failed validation.");
        }
    }

    private void PreserveMalformed(string path, string reason)
    {
        CorruptDataQuarantine.TryPreserve(
            _paths,
            path,
            "RuntimeCompletion",
            reason,
            out _);
    }

    private string GetPath(Guid launchSessionId) =>
        Path.Combine(Root, $"{launchSessionId:N}.json");
}
