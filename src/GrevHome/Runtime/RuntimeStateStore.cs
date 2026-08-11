using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Runtime;

public sealed record RuntimeSessionRecoveryRecord(
    Guid LaunchSessionId,
    string AppId,
    string AppName,
    string? PrimaryGrevId,
    IReadOnlyList<LaunchParticipant> Participants,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastObservedAliveAtUtc,
    LaunchSessionState State,
    int RootProcessId,
    IReadOnlyList<RuntimeProcessIdentity> Processes,
    string? ProcessName = null);

public sealed class RuntimeStateStore
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public RuntimeStateStore(AppPaths paths)
    {
        _paths = paths;
    }

    public string StateFile => Path.Combine(_paths.RuntimeData, "sessions.json");

    public IReadOnlyList<RuntimeSessionRecoveryRecord> Load()
    {
        _paths.EnsureMachineLayout();
        if (!File.Exists(StateFile))
        {
            return Array.Empty<RuntimeSessionRecoveryRecord>();
        }

        try
        {
            using var stream = File.OpenRead(StateFile);
            return JsonSerializer.Deserialize<List<RuntimeSessionRecoveryRecord>>(stream, _jsonOptions)
                   ?? new List<RuntimeSessionRecoveryRecord>();
        }
        catch (JsonException)
        {
            // A damaged recovery file must never stop Grev Home from launching.
            // Leave it untouched for diagnosis; the next successful runtime write will replace it.
            return Array.Empty<RuntimeSessionRecoveryRecord>();
        }
        catch (IOException)
        {
            return Array.Empty<RuntimeSessionRecoveryRecord>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<RuntimeSessionRecoveryRecord>();
        }
    }

    public void Save(IReadOnlyList<RuntimeSessionRecoveryRecord> sessions)
    {
        _paths.EnsureMachineLayout();
        var temporaryPath = StateFile + ".tmp";

        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, sessions, _jsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, StateFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
