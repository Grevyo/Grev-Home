using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using GrevHome.Storage;

namespace GrevHome.Presentation;

public sealed record DashboardTileDefinition(string Id, string Name, string Detail, string Color);
public sealed record DashboardTileOverride(string? DisplayName = null, string? TileColor = null, string? TileMediaFile = null);
public sealed record ResolvedDashboardTile(string Id, string DisplayName, string Detail, string TileColor, string? TileMediaPath, bool HasOverride);

public static class DashboardTileCatalog
{
    public static IReadOnlyList<DashboardTileDefinition> All { get; } =
    [
        new("your-games", "Your Games", "Open your complete game library", "#243451"),
        new("installed-apps", "Installed Apps", "Apps, emulators and individual games", "#243451"),
        new("grev-store", "Grev Store", "Browse supported apps", "#243451"),
        new("files", "Files", "Browse local files and folders", "#243451"),
        new("running-apps", "Running Apps", "0 active", "#151923"),
        new("activity-center", "Activity Center", "Notifications and downloads", "#151923"),
        new("app-killer", "App Killer", "Manage or force-close a stuck app", "#151923"),
        new("settings", "Settings", "Grev Home and controller settings", "#151923"),
        new("admin-console", "Admin Console", "Machine, apps and account administration", "#151923")
    ];

    public static DashboardTileDefinition Get(string id) =>
        All.First(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed class DashboardTilePresentationService
{
    private const long MaxAssetBytes = 25L * 1024L * 1024L;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public DashboardTilePresentationService(AppPaths paths) => _paths = paths;

    public async Task<IReadOnlyDictionary<string, ResolvedDashboardTile>> ResolveAllAsync(string grevId, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, ResolvedDashboardTile>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in DashboardTileCatalog.All)
        {
            result[definition.Id] = await ResolveAsync(grevId, definition.Id, cancellationToken);
        }
        return result;
    }

    public async Task<ResolvedDashboardTile> ResolveAsync(string grevId, string tileId, CancellationToken cancellationToken = default)
    {
        var definition = DashboardTileCatalog.Get(tileId);
        var value = await ReadAsync(grevId, tileId, cancellationToken);
        var root = GetTileRoot(grevId, tileId);
        var media = string.IsNullOrWhiteSpace(value.TileMediaFile) ? null : Path.Combine(root, Path.GetFileName(value.TileMediaFile));
        if (media is not null && !File.Exists(media)) media = null;
        return new ResolvedDashboardTile(
            definition.Id,
            string.IsNullOrWhiteSpace(value.DisplayName) ? definition.Name : value.DisplayName.Trim(),
            definition.Detail,
            NormalizeColor(value.TileColor) ?? definition.Color,
            media,
            !string.IsNullOrWhiteSpace(value.DisplayName) || !string.IsNullOrWhiteSpace(value.TileColor) || media is not null);
    }

    public async Task SaveAsync(string grevId, string tileId, string displayName, string tileColor, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(grevId, tileId, cancellationToken);
        var name = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        if (name is { Length: > 100 }) throw new InvalidOperationException("Dashboard tile names must be 100 characters or fewer.");
        await WriteAsync(grevId, tileId, current with { DisplayName = name, TileColor = NormalizeColor(tileColor) }, cancellationToken);
    }

    public async Task SaveMediaAsync(string grevId, string tileId, string sourcePath, CancellationToken cancellationToken = default)
    {
        ValidateImage(sourcePath);
        var root = GetTileRoot(grevId, tileId);
        Directory.CreateDirectory(root);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var target = Path.Combine(root, "tile" + extension);
        var temporary = Path.Combine(root, $"tile-{Guid.NewGuid():N}{extension}.tmp");
        try
        {
            File.Copy(sourcePath, temporary, false);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        foreach (var old in Directory.EnumerateFiles(root, "tile.*").Where(path => !string.Equals(path, target, StringComparison.OrdinalIgnoreCase))) File.Delete(old);
        var current = await ReadAsync(grevId, tileId, cancellationToken);
        await WriteAsync(grevId, tileId, current with { TileMediaFile = Path.GetFileName(target) }, cancellationToken);
    }

    public async Task UseExistingMediaAsync(string grevId, string tileId, string sourcePath, CancellationToken cancellationToken = default) =>
        await SaveMediaAsync(grevId, tileId, sourcePath, cancellationToken);

    public IReadOnlyList<string> GetReusableMedia(string grevId)
    {
        var root = GetDashboardRoot(grevId);
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "tile.*", SearchOption.AllDirectories)
            .Where(path => Extensions.Contains(Path.GetExtension(path))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task ResetAsync(string grevId, string tileId, CancellationToken cancellationToken = default)
    {
        var root = GetTileRoot(grevId, tileId);
        if (Directory.Exists(root)) await Task.Run(() => Directory.Delete(root, true), cancellationToken);
    }

    private async Task<DashboardTileOverride> ReadAsync(string grevId, string tileId, CancellationToken cancellationToken)
    {
        var path = GetMetadata(grevId, tileId);
        if (!File.Exists(path)) return new();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<DashboardTileOverride>(stream, _json, cancellationToken) ?? new();
        }
        catch (JsonException) { return new(); }
    }

    private async Task WriteAsync(string grevId, string tileId, DashboardTileOverride value, CancellationToken cancellationToken)
    {
        var path = GetMetadata(grevId, tileId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await using var stream = File.Create(temporary);
            await JsonSerializer.SerializeAsync(stream, value, _json, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void ValidateImage(string source)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("That dashboard artwork no longer exists.", source);
        if (!Extensions.Contains(Path.GetExtension(source))) throw new InvalidOperationException("Dashboard artwork must be PNG, JPG, JPEG, BMP or GIF.");
        var info = new FileInfo(source);
        if (info.Length <= 0 || info.Length > MaxAssetBytes) throw new InvalidOperationException("Dashboard artwork must be no more than 25 MB.");
        using var stream = File.OpenRead(source);
        if (BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Count == 0)
            throw new InvalidOperationException("That file is not a readable image.");
    }

    private static string? NormalizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var color = value.Trim().ToUpperInvariant();
        if (color.Length is not (7 or 9) || color[0] != '#' || !color[1..].All(Uri.IsHexDigit))
            throw new InvalidOperationException("Tile colour must be #RRGGBB or #AARRGGBB.");
        return color;
    }

    private string GetDashboardRoot(string grevId) => Path.Combine(_paths.GetProfilePresentation(grevId), "Dashboard");
    private string GetTileRoot(string grevId, string tileId) => Path.Combine(GetDashboardRoot(grevId), tileId);
    private string GetMetadata(string grevId, string tileId) => Path.Combine(GetTileRoot(grevId, tileId), "presentation.json");
}
