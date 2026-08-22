using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Notifications;

public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record GrevNotification(
    string Id,
    DateTimeOffset CreatedAtUtc,
    NotificationSeverity Severity,
    string Source,
    string Title,
    string Message,
    string? GrevId,
    IReadOnlyList<string>? ReadByGrevIds);

public sealed record NotificationSnapshot(
    int UnreadCount,
    IReadOnlyList<GrevNotification> Items)
{
    public static NotificationSnapshot Empty { get; } = new(0, Array.Empty<GrevNotification>());
}

internal sealed record NotificationStore(
    int SchemaVersion,
    IReadOnlyList<GrevNotification>? Items);

/// <summary>
/// Persistent Grev Home notification backbone. Notifications may be machine-wide or scoped to one
/// GrevID. Read state is tracked per GrevID so one account acknowledging a machine notification
/// never hides it from every other local account.
/// </summary>
public sealed class NotificationService
{
    private const int SchemaVersion = 1;
    private const int MaximumStoredNotifications = 250;

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public NotificationService(AppPaths paths)
    {
        _paths = paths;
    }

    public event Action? Changed;

    public async Task<GrevNotification> PublishAsync(
        NotificationSeverity severity,
        string source,
        string title,
        string message,
        string? grevId = null,
        CancellationToken cancellationToken = default)
    {
        source = NormalizeRequired(source, nameof(source), 60);
        title = NormalizeRequired(title, nameof(title), 120);
        message = NormalizeRequired(message, nameof(message), 1000);
        grevId = string.IsNullOrWhiteSpace(grevId) ? null : grevId.Trim();

        var notification = new GrevNotification(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            severity,
            source,
            title,
            message,
            grevId,
            Array.Empty<string>());

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await ReadStoreAsync(cancellationToken);
            var items = GetItems(store)
                .Append(notification)
                .OrderByDescending(item => item.CreatedAtUtc)
                .Take(MaximumStoredNotifications)
                .ToArray();
            await WriteStoreAsync(new NotificationStore(SchemaVersion, items), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke();
        return notification;
    }

    public async Task<NotificationSnapshot> GetForGrevIdAsync(
        string? grevId,
        int maximumItems = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(grevId))
        {
            return NotificationSnapshot.Empty;
        }

        grevId = grevId.Trim();
        maximumItems = Math.Clamp(maximumItems, 1, 100);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await ReadStoreAsync(cancellationToken);
            var visible = GetItems(store)
                .Where(item => item.GrevId is null ||
                               string.Equals(item.GrevId, grevId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToArray();
            var unread = visible.Count(item => !IsReadBy(item, grevId));
            return new NotificationSnapshot(unread, visible.Take(maximumItems).ToArray());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkReadAsync(
        string notificationId,
        string grevId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(notificationId) || string.IsNullOrWhiteSpace(grevId))
        {
            return;
        }

        var changed = false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await ReadStoreAsync(cancellationToken);
            var items = GetItems(store).ToArray();
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                if (!string.Equals(item.Id, notificationId, StringComparison.OrdinalIgnoreCase) ||
                    (item.GrevId is not null &&
                     !string.Equals(item.GrevId, grevId, StringComparison.OrdinalIgnoreCase)) ||
                    IsReadBy(item, grevId))
                {
                    continue;
                }

                items[index] = item with
                {
                    ReadByGrevIds = GetReadByIds(item)
                        .Append(grevId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
                changed = true;
                break;
            }

            if (changed)
            {
                await WriteStoreAsync(new NotificationStore(SchemaVersion, items), cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public async Task MarkAllReadAsync(string grevId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(grevId))
        {
            return;
        }

        var changed = false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await ReadStoreAsync(cancellationToken);
            var items = GetItems(store).ToArray();
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                var visible = item.GrevId is null ||
                              string.Equals(item.GrevId, grevId, StringComparison.OrdinalIgnoreCase);
                if (!visible || IsReadBy(item, grevId))
                {
                    continue;
                }

                items[index] = item with
                {
                    ReadByGrevIds = GetReadByIds(item)
                        .Append(grevId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
                changed = true;
            }

            if (changed)
            {
                await WriteStoreAsync(new NotificationStore(SchemaVersion, items), cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public static bool IsReadBy(GrevNotification notification, string grevId) =>
        GetReadByIds(notification).Any(id =>
            string.Equals(id, grevId, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> GetReadByIds(GrevNotification notification) =>
        notification.ReadByGrevIds ?? Array.Empty<string>();

    private static IReadOnlyList<GrevNotification> GetItems(NotificationStore store) =>
        store.Items ?? Array.Empty<GrevNotification>();

    private async Task<NotificationStore> ReadStoreAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureMachineLayout();
        if (!File.Exists(_paths.NotificationFile))
        {
            return EmptyStore();
        }

        try
        {
            await using var stream = File.OpenRead(_paths.NotificationFile);
            var store = await JsonSerializer.DeserializeAsync<NotificationStore>(stream, _jsonOptions, cancellationToken);
            if (store is null)
            {
                return RecoverMalformedStore("Notification JSON contained no usable store.");
            }
            if (store.SchemaVersion > SchemaVersion)
            {
                throw new InvalidDataException(
                    $"Notification schema {store.SchemaVersion} is newer than this Grev Home build supports ({SchemaVersion}).");
            }
            if (store.SchemaVersion <= 0)
            {
                return RecoverMalformedStore($"Invalid notification schema version {store.SchemaVersion}.");
            }

            return store;
        }
        catch (JsonException ex)
        {
            return RecoverMalformedStore($"Notification JSON could not be parsed: {ex.Message}");
        }
    }

    private NotificationStore RecoverMalformedStore(string reason)
    {
        if (!CorruptDataQuarantine.TryPreserve(_paths, _paths.NotificationFile, "Notifications", reason, out _))
        {
            throw new InvalidDataException(
                "Grev Home found malformed notification data and could not preserve a recovery copy. The file was left untouched.");
        }
        return EmptyStore();
    }

    private static NotificationStore EmptyStore() =>
        new(SchemaVersion, Array.Empty<GrevNotification>());

    private async Task WriteStoreAsync(NotificationStore store, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.NotificationData);
        var temporaryPath = _paths.NotificationFile + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, store, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _paths.NotificationFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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
}
