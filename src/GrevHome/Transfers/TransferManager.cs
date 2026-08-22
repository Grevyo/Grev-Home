using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GrevHome.Notifications;
using GrevHome.Storage;

namespace GrevHome.Transfers;

public enum TransferState
{
    Queued,
    Downloading,
    Completed,
    Failed,
    Cancelled
}

public sealed record TransferItem(
    string Id,
    string DisplayName,
    string SourceUri,
    string RelativeDestination,
    string? OwnerGrevId,
    TransferState State,
    long BytesReceived,
    long? TotalBytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorMessage);

public sealed record TransferSnapshot(IReadOnlyList<TransferItem> Items)
{
    public static TransferSnapshot Empty { get; } = new(Array.Empty<TransferItem>());

    public int ActiveCount => Items.Count(item => item.State == TransferState.Downloading);
    public int QueuedCount => Items.Count(item => item.State == TransferState.Queued);
    public int FailedCount => Items.Count(item => item.State == TransferState.Failed);
}

internal sealed record TransferStore(int SchemaVersion, IReadOnlyList<TransferItem> Items);

/// <summary>
/// Central download/transfer backbone. Destinations are constrained beneath Grev Home Downloads,
/// queue state survives shell restarts, interrupted work is safely re-queued, and HTTP partials
/// resume when the remote server supports byte ranges.
/// </summary>
public sealed class TransferManager : IDisposable
{
    private const int SchemaVersion = 1;
    private static readonly TimeSpan ProgressPersistInterval = TimeSpan.FromMilliseconds(500);

    private readonly AppPaths _paths;
    private readonly NotificationService _notifications;
    private readonly HttpClient _httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeCancellation =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private List<TransferItem> _items = new();
    private Task? _workerTask;
    private bool _initialized;
    private bool _disposed;

    public TransferManager(AppPaths paths, NotificationService notifications)
    {
        _paths = paths;
        _notifications = notifications;
    }

    public event Action<TransferSnapshot>? SnapshotChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var hasQueuedWork = false;

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var store = await ReadStoreAsync(cancellationToken);
            _items = store.Items
                .Select(item => item.State == TransferState.Downloading
                    ? item with
                    {
                        State = TransferState.Queued,
                        StartedAtUtc = null,
                        CompletedAtUtc = null,
                        ErrorMessage = "Recovered after Grev Home restarted."
                    }
                    : item)
                .ToList();
            hasQueuedWork = _items.Any(item => item.State == TransferState.Queued);
            _initialized = true;
            await WriteStoreAsync(cancellationToken);
        }
        finally
        {
            _stateGate.Release();
        }

        _workerTask = Task.Run(ProcessQueueAsync);
        if (hasQueuedWork)
        {
            SignalWorker();
        }
        await RaiseSnapshotChangedAsync(cancellationToken);
    }

    public async Task<TransferSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            return CreateSnapshotLocked();
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<TransferItem> EnqueueDownloadAsync(
        Uri source,
        string relativeDestination,
        string displayName,
        string? ownerGrevId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        await EnsureInitializedAsync(cancellationToken);

        if (!source.IsAbsoluteUri ||
            (source.Scheme != Uri.UriSchemeHttps && source.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Downloads must use an absolute HTTP or HTTPS URI.", nameof(source));
        }

        if (!string.IsNullOrEmpty(source.UserInfo))
        {
            throw new ArgumentException("Credentials must not be embedded in a download URI.", nameof(source));
        }

        displayName = NormalizeRequired(displayName, nameof(displayName), 140);
        relativeDestination = NormalizeRelativeDestination(relativeDestination);
        _ = ResolveDestination(relativeDestination);
        ownerGrevId = string.IsNullOrWhiteSpace(ownerGrevId) ? null : ownerGrevId.Trim();

        var item = new TransferItem(
            Guid.NewGuid().ToString("N"),
            displayName,
            source.AbsoluteUri,
            relativeDestination,
            ownerGrevId,
            TransferState.Queued,
            0,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            _items.Add(item);
            await WriteStoreAsync(cancellationToken);
        }
        finally
        {
            _stateGate.Release();
        }

        await RaiseSnapshotChangedAsync(cancellationToken);
        SignalWorker();
        return item;
    }

    public async Task CancelAsync(string transferId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(transferId))
        {
            return;
        }

        var changed = false;
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var index = FindIndexLocked(transferId);
            if (index < 0)
            {
                return;
            }

            var item = _items[index];
            if (item.State == TransferState.Queued)
            {
                _items[index] = item with
                {
                    State = TransferState.Cancelled,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    ErrorMessage = null
                };
                changed = true;
                await WriteStoreAsync(cancellationToken);
            }
        }
        finally
        {
            _stateGate.Release();
        }

        if (_activeCancellation.TryGetValue(transferId, out var activeCancellation))
        {
            activeCancellation.Cancel();
        }

        if (changed)
        {
            await RaiseSnapshotChangedAsync(cancellationToken);
        }
    }

    public async Task RetryAsync(string transferId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var changed = false;

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var index = FindIndexLocked(transferId);
            if (index < 0)
            {
                return;
            }

            var item = _items[index];
            if (item.State is not (TransferState.Failed or TransferState.Cancelled))
            {
                return;
            }

            _items[index] = item with
            {
                State = TransferState.Queued,
                StartedAtUtc = null,
                CompletedAtUtc = null,
                ErrorMessage = null
            };
            changed = true;
            await WriteStoreAsync(cancellationToken);
        }
        finally
        {
            _stateGate.Release();
        }

        if (changed)
        {
            await RaiseSnapshotChangedAsync(cancellationToken);
            SignalWorker();
        }
    }

    public async Task ClearFinishedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        List<TransferItem> removed;

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            removed = _items
                .Where(item => item.State is TransferState.Completed or TransferState.Failed or TransferState.Cancelled)
                .ToList();
            if (removed.Count == 0)
            {
                return;
            }

            var removedIds = removed.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _items.RemoveAll(item => removedIds.Contains(item.Id));
            await WriteStoreAsync(cancellationToken);
        }
        finally
        {
            _stateGate.Release();
        }

        foreach (var item in removed)
        {
            TryDeletePartial(item);
        }
        await RaiseSnapshotChangedAsync(cancellationToken);
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            while (!_lifetimeCts.IsCancellationRequested)
            {
                await _queueSignal.WaitAsync(_lifetimeCts.Token);

                while (!_lifetimeCts.IsCancellationRequested)
                {
                    var started = await TryStartNextQueuedAsync(_lifetimeCts.Token);
                    if (started is null)
                    {
                        break;
                    }

                    var (item, itemCancellation) = started.Value;
                    await RaiseSnapshotChangedAsync(_lifetimeCts.Token);

                    try
                    {
                        await DownloadOneAsync(item, itemCancellation.Token);
                        await SetTerminalStateAsync(item.Id, TransferState.Completed, null);
                        await TryPublishTransferNotificationAsync(
                            NotificationSeverity.Success,
                            item,
                            "Download complete",
                            $"{item.DisplayName} finished downloading.");
                    }
                    catch (OperationCanceledException) when (!_lifetimeCts.IsCancellationRequested)
                    {
                        await SetTerminalStateAsync(item.Id, TransferState.Cancelled, null);
                    }
                    catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
                    {
                        await RequeueInterruptedAsync(item.Id);
                        return;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        await SetTerminalStateAsync(item.Id, TransferState.Failed, ex.Message);
                        await TryPublishTransferNotificationAsync(
                            NotificationSeverity.Error,
                            item,
                            "Download failed",
                            $"{item.DisplayName}: {ex.Message}");
                    }
                    finally
                    {
                        if (_activeCancellation.TryRemove(item.Id, out var cancellation))
                        {
                            cancellation.Dispose();
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Normal shell shutdown.
        }
    }

    private async Task<(TransferItem Item, CancellationTokenSource Cancellation)?> TryStartNextQueuedAsync(
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var index = _items.FindIndex(candidate => candidate.State == TransferState.Queued);
            if (index < 0)
            {
                return null;
            }

            var item = _items[index] with
            {
                State = TransferState.Downloading,
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = null,
                ErrorMessage = null
            };
            _items[index] = item;

            var itemCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            if (!_activeCancellation.TryAdd(item.Id, itemCancellation))
            {
                itemCancellation.Dispose();
                throw new InvalidOperationException("Transfer cancellation state is already active for this item.");
            }

            await WriteStoreAsync(cancellationToken);
            return (item, itemCancellation);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task DownloadOneAsync(TransferItem item, CancellationToken cancellationToken)
    {
        var destination = ResolveDestination(item.RelativeDestination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partialPath = destination + ".grevpartial";
        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, item.SourceUri);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var resuming = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resuming)
        {
            existingLength = 0;
        }

        response.EnsureSuccessStatusCode();
        long? totalBytes = response.Content.Headers.ContentLength is long responseLength
            ? checked(existingLength + responseLength)
            : null;

        await UpdateProgressAsync(item.Id, existingLength, totalBytes, cancellationToken);

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(
            partialPath,
            resuming ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var buffer = new byte[81920];
        var received = existingLength;
        var lastPersistedAt = DateTimeOffset.UtcNow;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            var now = DateTimeOffset.UtcNow;
            if (now - lastPersistedAt >= ProgressPersistInterval)
            {
                await UpdateProgressAsync(item.Id, received, totalBytes, cancellationToken);
                lastPersistedAt = now;
            }
        }

        await target.FlushAsync(cancellationToken);
        await UpdateProgressAsync(item.Id, received, totalBytes ?? received, cancellationToken);
        File.Move(partialPath, destination, overwrite: true);
    }

    private async Task UpdateProgressAsync(
        string transferId,
        long bytesReceived,
        long? totalBytes,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var index = FindIndexLocked(transferId);
            if (index < 0)
            {
                return;
            }

            _items[index] = _items[index] with
            {
                BytesReceived = Math.Max(0, bytesReceived),
                TotalBytes = totalBytes is null ? null : Math.Max(bytesReceived, totalBytes.Value)
            };
            await WriteStoreAsync(cancellationToken);
        }
        finally
        {
            _stateGate.Release();
        }

        await RaiseSnapshotChangedAsync(cancellationToken);
    }

    private async Task SetTerminalStateAsync(string transferId, TransferState state, string? errorMessage)
    {
        await _stateGate.WaitAsync(CancellationToken.None);
        try
        {
            var index = FindIndexLocked(transferId);
            if (index < 0)
            {
                return;
            }

            _items[index] = _items[index] with
            {
                State = state,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ErrorMessage = errorMessage
            };
            await WriteStoreAsync(CancellationToken.None);
        }
        finally
        {
            _stateGate.Release();
        }

        await RaiseSnapshotChangedAsync(CancellationToken.None);
    }

    private async Task RequeueInterruptedAsync(string transferId)
    {
        await _stateGate.WaitAsync(CancellationToken.None);
        try
        {
            var index = FindIndexLocked(transferId);
            if (index < 0)
            {
                return;
            }

            _items[index] = _items[index] with
            {
                State = TransferState.Queued,
                StartedAtUtc = null,
                CompletedAtUtc = null,
                ErrorMessage = "Interrupted while Grev Home was closing."
            };
            await WriteStoreAsync(CancellationToken.None);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task TryPublishTransferNotificationAsync(
        NotificationSeverity severity,
        TransferItem item,
        string title,
        string message)
    {
        try
        {
            await _notifications.PublishAsync(
                severity,
                "Downloads",
                title,
                message,
                item.OwnerGrevId,
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Notification persistence must never change a completed/failed transfer into another state.
        }
    }

    private void SignalWorker()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _queueSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // An existing signal is already enough to make the worker drain every queued item.
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    private TransferSnapshot CreateSnapshotLocked() => new(
        _items
            .OrderBy(item => item.State == TransferState.Downloading ? 0 : item.State == TransferState.Queued ? 1 : 2)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ToArray());

    private async Task RaiseSnapshotChangedAsync(CancellationToken cancellationToken)
    {
        TransferSnapshot snapshot;
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            snapshot = CreateSnapshotLocked();
        }
        finally
        {
            _stateGate.Release();
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    private int FindIndexLocked(string transferId) =>
        _items.FindIndex(item => string.Equals(item.Id, transferId, StringComparison.OrdinalIgnoreCase));

    private string ResolveDestination(string relativeDestination)
    {
        var root = Path.GetFullPath(_paths.Downloads);
        var destination = Path.GetFullPath(Path.Combine(root, relativeDestination));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Transfer destination must stay inside Grev Home Downloads.");
        }

        return destination;
    }

    private static string NormalizeRelativeDestination(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            throw new ArgumentException("A relative download destination is required.", nameof(value));
        }

        value = value.Trim();
        if (value.Length > 240)
        {
            throw new ArgumentException("Download destination is too long.", nameof(value));
        }

        return value;
    }

    private static string NormalizeRequired(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        value = value.Trim();
        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
        }

        return value;
    }

    private async Task<TransferStore> ReadStoreAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureMachineLayout();
        if (!File.Exists(_paths.TransferStateFile))
        {
            return EmptyStore();
        }

        try
        {
            await using var stream = File.OpenRead(_paths.TransferStateFile);
            var store = await JsonSerializer.DeserializeAsync<TransferStore>(stream, _jsonOptions, cancellationToken);
            if (store is null || store.Items is null)
            {
                return RecoverMalformedStore("Transfer JSON contained no usable queue state.");
            }
            if (store.SchemaVersion > SchemaVersion)
            {
                throw new InvalidDataException(
                    $"Transfer schema {store.SchemaVersion} is newer than this Grev Home build supports ({SchemaVersion}).");
            }
            if (store.SchemaVersion <= 0)
            {
                return RecoverMalformedStore($"Invalid transfer schema version {store.SchemaVersion}.");
            }

            return store;
        }
        catch (JsonException ex)
        {
            return RecoverMalformedStore($"Transfer JSON could not be parsed: {ex.Message}");
        }
        catch (IOException)
        {
            return EmptyStore();
        }
    }

    private TransferStore RecoverMalformedStore(string reason)
    {
        if (!CorruptDataQuarantine.TryPreserve(_paths, _paths.TransferStateFile, "Transfers", reason, out _))
        {
            throw new InvalidDataException(
                "Grev Home found malformed transfer state and could not preserve a recovery copy. The queue file was left untouched.");
        }
        return EmptyStore();
    }

    private static TransferStore EmptyStore() =>
        new(SchemaVersion, Array.Empty<TransferItem>());

    private async Task WriteStoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.TransferData);
        var temporaryPath = _paths.TransferStateFile + ".tmp";
        var store = new TransferStore(SchemaVersion, _items.ToArray());
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, store, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _paths.TransferStateFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void TryDeletePartial(TransferItem item)
    {
        try
        {
            var partialPath = ResolveDestination(item.RelativeDestination) + ".grevpartial";
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Clearing history must not destabilize the shell because one partial file is locked.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCts.Cancel();
        foreach (var cancellation in _activeCancellation.Values)
        {
            cancellation.Cancel();
        }

        try
        {
            _workerTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected when the worker is idle on _queueSignal during normal shell shutdown.
        }

        foreach (var cancellation in _activeCancellation.Values)
        {
            cancellation.Dispose();
        }
        _activeCancellation.Clear();

        _httpClient.Dispose();
        _queueSignal.Dispose();
        _stateGate.Dispose();
        _lifetimeCts.Dispose();
    }
}
