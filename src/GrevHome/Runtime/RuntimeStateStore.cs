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
    string? ProcessName = null,
    IReadOnlyList<string>? AdditionalProcessNames = null,
    bool? TrackDescendantProcesses = null,
    bool? ForceKillEntireProcessTree = null,
    long AccumulatedSuspendedSeconds = 0,
    DateTimeOffset? SuspendedAtUtc = null);

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
                   ?? RecoverMalformedState("Runtime recovery JSON contained no usable session list.");
        }
        catch (JsonException ex)
        {
            return RecoverMalformedState($"Runtime recovery JSON could not be parsed: {ex.Message}");
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
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       16 * 1024,
                       FileOptions.WriteThrough))
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

    private IReadOnlyList<RuntimeSessionRecoveryRecord> RecoverMalformedState(string reason)
    {
        // Runtime recovery is reconstructable from live processes, but the malformed bytes are still
        // preserved before Grev Home writes a clean recovery snapshot later in this launch.
        CorruptDataQuarantine.TryPreserve(_paths, StateFile, "Runtime", reason, out _);
        return Array.Empty<RuntimeSessionRecoveryRecord>();
    }
}
