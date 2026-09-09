using System.IO;
using System.Text;
using System.Text.Json;
using GrevHome.Runtime;
using GrevHome.Storage;

namespace GrevHome.Diagnostics;

public sealed record RuntimeRecoveryAuditEntry(
    int SchemaVersion,
    DateTimeOffset RecordedAtUtc,
    string EventKind,
    Guid LaunchSessionId,
    string AppId,
    string AppName,
    string? PrimaryGrevId,
    string Message);

/// <summary>
/// Append-only evidence for exceptional runtime recovery paths. Normal app completion is deliberately
/// not recorded here; the journal exists so a later machine-health/soak-test investigation can prove
/// that a completion was deferred or replayed even when recovery happened before the shell UI existed.
/// Diagnostic failure must never block or replace the actual runtime recovery path.
/// </summary>
public sealed class RuntimeRecoveryJournal
{
    private const int SchemaVersion = 1;
    private readonly string _journalFile;
    private readonly object _writeGate = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public RuntimeRecoveryJournal(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _journalFile = Path.Combine(paths.Data, "Diagnostics", "runtime-recovery.jsonl");
    }

    public string JournalFile => _journalFile;

    public void TryAppend(string eventKind, LaunchSessionSnapshot snapshot, string? message = null)
    {
        if (snapshot.LaunchSessionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(snapshot.AppId) ||
            string.IsNullOrWhiteSpace(snapshot.AppName) ||
            string.IsNullOrWhiteSpace(eventKind))
        {
            return;
        }

        var entry = new RuntimeRecoveryAuditEntry(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            eventKind.Trim(),
            snapshot.LaunchSessionId,
            snapshot.AppId,
            snapshot.AppName,
            snapshot.PrimaryGrevId,
            string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim());

        try
        {
            lock (_writeGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_journalFile)!);
                using var stream = new FileStream(
                    _journalFile,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    16 * 1024,
                    FileOptions.WriteThrough);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.WriteLine(JsonSerializer.Serialize(entry, _json));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnostics are intentionally secondary to the pending-completion envelope and the
            // idempotent local stores. Never turn a successful/deferred recovery into a runtime crash.
        }
    }
}
