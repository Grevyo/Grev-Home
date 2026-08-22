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
    IReadOnlyList<string> ReadByGrevIds);

public sealed record NotificationSnapshot(
    int UnreadCount,
    IReadOnlyList<GrevNotification> Items)
{
    public static NotificationSnapshot Empty { get; } = new(0, Array.Empty<GrevNotification>());
}

internal sealed record NotificationStore(
    int SchemaVersion,
    IReadOnlyList<GrevNotification> Items);

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
            var items = store.Items
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
            var visible = store.Items
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
            var items = store.Items.ToArray();
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
                    ReadByGrevIds = item.ReadByGrevIds
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
            var items = store.Items.ToArray();
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
                    ReadByGrevIds = item.ReadByGrevIds
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
        notification.ReadByGrevIds.Any(id =>
            string.Equals(id, grevId, StringComparison.OrdinalIgnoreCase));

    private async Task<NotificationStore> ReadStoreAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureMachineLayout();
        if (!File.Exists(_paths.NotificationFile))
        {
            return new NotificationStore(SchemaVersion, Array.Empty<GrevNotification>());
        }

        try
        {
            await using var stream = File.OpenRead(_paths.NotificationFile);
            var store = await JsonSerializer.DeserializeAsync<NotificationStore>(stream, _jsonOptions, cancellationToken);
            if (store is null || store.SchemaVersion != SchemaVersion)
            {
                return new NotificationStore(SchemaVersion, Array.Empty<GrevNotification>());
            }

            return store with { Items = store.Items ?? Array.Empty<GrevNotification>() };
        }
        catch (JsonException)
        {
            return new NotificationStore(SchemaVersion, Array.Empty<GrevNotification>());
        }
        catch (IOException)
        {
            return new NotificationStore(SchemaVersion, Array.Empty<GrevNotification>());
        }
    }

    private async Task WriteStoreAsync(NotificationStore store, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.NotificationData);
        var temporaryPath = _paths.NotificationFile + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, store, _jsonOptions, cancellationToken);
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
