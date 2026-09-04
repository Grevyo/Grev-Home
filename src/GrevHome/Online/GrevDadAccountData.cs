using System.Text.Json;
using GrevHome.Runtime;
using GrevHome.Storage;

namespace GrevHome.Online;

public sealed record CloudAppStat(string AppId, string AppName, long TotalSeconds, int SessionCount, long LastPlayedAt);
public sealed record CloudProfileSource(string GrevId, long? ProfileCreatedAt, long TotalSeconds,
    int CompletedSessions, int UniqueApps, CloudAppStat[] Apps, long UpdatedAt);
public sealed record GrevDadAccountData(bool Ok, int ApiVersion, string UserId, string Username,
    string DisplayName, long AccountCreatedAt, long DownloadedAt, CloudProfileSource[] Sources);

// Read-only cloud projection: never imported into the local journal or uploaded as local playtime.
public static class GrevDadAccountDataStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string FilePath(AppPaths paths, string grevId) =>
        Path.Combine(paths.GetProfileConnections(grevId), "GrevDad", "account-data.json");

    public static async Task SaveAsync(AppPaths paths, string grevId, GrevDadAccountData data, CancellationToken ct)
    {
        if (!data.Ok || data.ApiVersion != 1 || string.IsNullOrWhiteSpace(data.UserId) || data.Sources is null ||
            data.Sources.Any(s => s.Apps is null || s.TotalSeconds < 0 || s.CompletedSessions < 0 ||
                s.Apps.Any(a => a.TotalSeconds < 0 || a.SessionCount < 0)))
            throw new InvalidDataException("Invalid cloud account data; previous statistics were preserved.");
        var path = FilePath(paths, grevId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, data, Json, ct);
                await stream.FlushAsync(ct);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<GrevDadAccountData?> ReadAsync(AppPaths paths, string grevId, CancellationToken ct = default)
    {
        try
        {
            var path = FilePath(paths,grevId);
            var linkPath = Path.Combine(Path.GetDirectoryName(path)!, "link.json");
            if (!File.Exists(path) || !File.Exists(linkPath)) return null;
            await using var linkStream = File.OpenRead(linkPath);
            using var link = await JsonDocument.ParseAsync(linkStream,cancellationToken:ct);
            var owner = link.RootElement.GetProperty("account").GetProperty("userId").GetString();
            await using var stream = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<GrevDadAccountData>(stream,Json,ct);
            return data?.Ok == true && data.ApiVersion == 1 && data.UserId == owner ? data : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
        { return null; }
    }

    public static async Task<PlaytimeSnapshot> CombineAsync(AppPaths paths, string grevId, PlaytimeSnapshot local, CancellationToken ct)
    {
        var data = await ReadAsync(paths,grevId,ct);
        if (data is null) return local;
        var combined = new Dictionary<string,AppPlaytimeStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in data.Sources)
        {
            var own = string.Equals(source.GrevId,grevId,StringComparison.OrdinalIgnoreCase);
            var apps = source.Apps.ToDictionary(a=>a.AppId,a=>new AppPlaytimeStat(a.AppId,a.AppName,a.TotalSeconds,
                a.SessionCount,DateTimeOffset.FromUnixTimeSeconds(a.LastPlayedAt)),StringComparer.OrdinalIgnoreCase);
            if (own)
                foreach (var app in local.Apps.Values)
                    apps[app.AppId] = apps.TryGetValue(app.AppId,out var old)
                        ? app with { TotalSeconds=Math.Max(app.TotalSeconds,old.TotalSeconds), SessionCount=Math.Max(app.SessionCount,old.SessionCount),
                            LastPlayedAtUtc=app.LastPlayedAtUtc > old.LastPlayedAtUtc ? app.LastPlayedAtUtc : old.LastPlayedAtUtc }
                        : app;
            // Legacy cloud snapshots have totals but no per-app breakdown. Preserve those totals
            // explicitly rather than assigning them to an invented game or dropping them.
            var missingSeconds = Math.Max(0,source.TotalSeconds-apps.Values.Sum(a=>a.TotalSeconds));
            var missingSessions = Math.Max(0,source.CompletedSessions-apps.Values.Sum(a=>a.SessionCount));
            if (missingSeconds>0 || missingSessions>0)
                apps["cloud-legacy-history"] = new("cloud-legacy-history","Earlier Grev Home activity",missingSeconds,
                    missingSessions,DateTimeOffset.FromUnixTimeSeconds(source.UpdatedAt));
            foreach(var app in apps.Values)
                combined[app.AppId] = combined.TryGetValue(app.AppId,out var old)
                    ? app with { TotalSeconds=checked(old.TotalSeconds+app.TotalSeconds),SessionCount=checked(old.SessionCount+app.SessionCount),
                        LastPlayedAtUtc=app.LastPlayedAtUtc > old.LastPlayedAtUtc ? app.LastPlayedAtUtc : old.LastPlayedAtUtc }
                    : app;
        }
        if (!data.Sources.Any(s=>string.Equals(s.GrevId,grevId,StringComparison.OrdinalIgnoreCase)))
            foreach(var app in local.Apps.Values)
                combined[app.AppId] = combined.TryGetValue(app.AppId,out var old)
                    ? app with { TotalSeconds=checked(old.TotalSeconds+app.TotalSeconds),SessionCount=checked(old.SessionCount+app.SessionCount) } : app;
        return local with { Apps=combined, UniqueAppsFloor=Math.Max(data.Sources.Select(s=>s.UniqueApps).DefaultIfEmpty().Max(),
            combined.Keys.Count(id=>id!="cloud-legacy-history")) };
    }
}
