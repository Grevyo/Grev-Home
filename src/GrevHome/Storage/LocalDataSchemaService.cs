using System.IO;
using System.Text.Json;

namespace GrevHome.Storage;

public sealed record LocalDataSchemaState(
    int SchemaVersion,
    string Product,
    DateTimeOffset FirstInitializedAtUtc,
    DateTimeOffset LastValidatedAtUtc);

public sealed record LocalDataMigrationLogEntry(
    int FromVersion,
    int ToVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string Description);

/// <summary>
/// Machine-wide version gate for Grev Home's persistent local data contracts. Migration steps are
/// sequential and marker updates happen only after a step succeeds, so an interrupted migration is
/// safely retryable. A build never rewrites a data root carrying a newer unsupported schema.
/// </summary>
public sealed class LocalDataSchemaService
{
    public const int CurrentSchemaVersion = 1;

    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public LocalDataSchemaService(AppPaths paths)
    {
        _paths = paths;
    }

    public string SchemaRoot => Path.Combine(_paths.Data, "Schema");
    public string StateFile => Path.Combine(SchemaRoot, "local-data-schema.json");
    public string MigrationLogFile => Path.Combine(SchemaRoot, "migration-history.jsonl");

    public async Task<LocalDataSchemaState> EnsureCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureMachineLayout();
        Directory.CreateDirectory(SchemaRoot);

        var state = await ReadStateAsync(cancellationToken);
        if (state is not null && state.SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"This Grev Home build supports local data schema {CurrentSchemaVersion}, but {_paths.Root} contains newer schema {state.SchemaVersion}. Nothing was migrated or downgraded.");
        }

        var firstInitialized = state?.FirstInitializedAtUtc ?? DateTimeOffset.UtcNow;
        var version = state?.SchemaVersion ?? 0;

        while (version < CurrentSchemaVersion)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = checked(version + 1);
            var started = DateTimeOffset.UtcNow;
            var description = await RunMigrationAsync(version, next, cancellationToken);
            var completed = DateTimeOffset.UtcNow;

            version = next;
            state = new LocalDataSchemaState(
                version,
                "Grev Home",
                firstInitialized,
                completed);
            await WriteStateAsync(state, cancellationToken);
            await AppendMigrationLogAsync(
                new LocalDataMigrationLogEntry(
                    next - 1,
                    next,
                    started,
                    completed,
                    description),
                cancellationToken);
        }

        state ??= new LocalDataSchemaState(
            CurrentSchemaVersion,
            "Grev Home",
            firstInitialized,
            DateTimeOffset.UtcNow);

        if (state.SchemaVersion == CurrentSchemaVersion)
        {
            state = state with { LastValidatedAtUtc = DateTimeOffset.UtcNow };
            await WriteStateAsync(state, cancellationToken);
        }

        return state;
    }

    private async Task<LocalDataSchemaState?> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(StateFile))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(StateFile);
            var value = await JsonSerializer.DeserializeAsync<LocalDataSchemaState>(stream, _json, cancellationToken);
            if (value is null || value.SchemaVersion < 0 ||
                !string.Equals(value.Product, "Grev Home", StringComparison.Ordinal))
            {
                return RecoverMalformedMarker("Local data schema marker contained invalid values.");
            }
            return value;
        }
        catch (JsonException ex)
        {
            return RecoverMalformedMarker($"Local data schema marker could not be parsed: {ex.Message}");
        }
    }

    private LocalDataSchemaState? RecoverMalformedMarker(string reason)
    {
        if (!CorruptDataQuarantine.TryPreserve(_paths, StateFile, "Schema", reason, out _))
        {
            throw new InvalidDataException(
                "Grev Home found a malformed local-data schema marker and could not preserve it. Startup was stopped to avoid guessing at the data version.");
        }
        return null;
    }

    private async Task<string> RunMigrationAsync(
        int fromVersion,
        int toVersion,
        CancellationToken cancellationToken)
    {
        if (fromVersion == 0 && toVersion == 1)
        {
            // Version 1 formalizes the already-existing GrevID-rooted layout. It deliberately does
            // not rewrite legacy/profile data; existing directories remain the source of truth.
            _paths.EnsureMachineLayout();
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            return "Established the Grev Home v1 local-data schema marker over the existing local-first layout without rewriting profile content.";
        }

        throw new InvalidOperationException(
            $"No local-data migration is registered from schema {fromVersion} to {toVersion}.");
    }

    private async Task WriteStateAsync(LocalDataSchemaState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(SchemaRoot);
        var temporary = StateFile + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, state, _json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, StateFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task AppendMigrationLogAsync(
        LocalDataMigrationLogEntry entry,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(SchemaRoot);
        await using var stream = new FileStream(
            MigrationLogFile,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using var writer = new StreamWriter(stream);
        await writer.WriteLineAsync(JsonSerializer.Serialize(entry, _json).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
