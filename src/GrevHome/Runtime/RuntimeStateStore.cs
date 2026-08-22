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
    private bool _writeBlockedByTransientReadFailure;

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
            _writeBlockedByTransientReadFailure = false;
            return Array.Empty<RuntimeSessionRecoveryRecord>();
        }

        try
        {
            using var stream = File.OpenRead(StateFile);
            var records = JsonSerializer.Deserialize<List<RuntimeSessionRecoveryRecord>>(stream, _jsonOptions);
            _writeBlockedByTransientReadFailure = false;
            return records ?? RecoverMalformedState("Runtime recovery JSON contained no usable session list.");
        }
        catch (JsonException ex)
        {
            _writeBlockedByTransientReadFailure = false;
            return RecoverMalformedState($"Runtime recovery JSON could not be parsed: {ex.Message}");
        }
        catch (IOException)
        {
            // Runtime state is authoritative for recovery. A temporary read problem must never be
            // converted into an empty state that a later heartbeat can overwrite.
            _writeBlockedByTransientReadFailure = true;
            return Array.Empty<RuntimeSessionRecoveryRecord>();
        }
        catch (UnauthorizedAccessException)
        {
            _writeBlockedByTransientReadFailure = true;
            return Array.Empty<RuntimeSessionRecoveryRecord>();
        }
    }

    public void Save(IReadOnlyList<RuntimeSessionRecoveryRecord> sessions)
    {
        _paths.EnsureMachineLayout();
        EnsureExistingStateIsSafeToReplace();

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
            _writeBlockedByTransientReadFailure = false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void EnsureExistingStateIsSafeToReplace()
    {
        if (!_writeBlockedByTransientReadFailure)
        {
            return;
        }

        if (!File.Exists(StateFile))
        {
            _writeBlockedByTransientReadFailure = false;
            return;
        }

        try
        {
            using var stream = File.OpenRead(StateFile);
            _ = JsonSerializer.Deserialize<List<RuntimeSessionRecoveryRecord>>(stream, _jsonOptions)
                ?? throw new InvalidDataException("Runtime recovery JSON contained no usable session list.");
            _writeBlockedByTransientReadFailure = false;
        }
        catch (JsonException ex)
        {
            // If the state has become readable and is genuinely malformed, preserve it before a
            // clean snapshot is allowed to replace it.
            if (!CorruptDataQuarantine.TryPreserve(
                    _paths,
                    StateFile,
                    "Runtime",
                    $"Runtime recovery JSON could not be parsed while revalidating a blocked write: {ex.Message}",
                    out _))
            {
                throw new IOException("Runtime recovery state is malformed and could not be preserved. Grev Home refused to overwrite it.", ex);
            }
            _writeBlockedByTransientReadFailure = false;
        }
        catch (InvalidDataException ex)
        {
            if (!CorruptDataQuarantine.TryPreserve(
                    _paths,
                    StateFile,
                    "Runtime",
                    ex.Message,
                    out _))
            {
                throw new IOException("Runtime recovery state could not be preserved. Grev Home refused to overwrite it.", ex);
            }
            _writeBlockedByTransientReadFailure = false;
        }
        catch (IOException ex)
        {
            throw new IOException("Runtime recovery state is temporarily unreadable. Grev Home refused to overwrite it.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException("Runtime recovery state is not currently accessible. Grev Home refused to overwrite it.", ex);
        }
    }

    private IReadOnlyList<RuntimeSessionRecoveryRecord> RecoverMalformedState(string reason)
    {
        // Runtime recovery is reconstructable from live processes, but the malformed bytes are still
        // preserved before Grev Home writes a clean recovery snapshot later in this launch.
        if (!CorruptDataQuarantine.TryPreserve(_paths, StateFile, "Runtime", reason, out _))
        {
            _writeBlockedByTransientReadFailure = true;
        }
        return Array.Empty<RuntimeSessionRecoveryRecord>();
    }
}
