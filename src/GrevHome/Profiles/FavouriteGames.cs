using System.IO;
using System.Text.Json;
using GrevHome.Storage;

namespace GrevHome.Profiles;

/// <summary>A starred game. ItemKey matches the item_key grev.dad stores (user_favourite_games), so
/// the same star toggles on both sides.</summary>
public sealed record FavouriteGame(
    string ItemKey,
    string Title,
    string Platform = "",
    string AppId = "",
    string ContentId = "",
    DateTimeOffset? AddedAtUtc = null);

public enum FavouriteChangeKind { Add, Remove }

/// <summary>A change made while grev.dad could not be reached, replayed on the next sync.</summary>
public sealed record FavouriteChange(FavouriteChangeKind Kind, FavouriteGame Game);

public sealed record FavouriteGamesState(
    IReadOnlyList<FavouriteGame> Games,
    IReadOnlyList<FavouriteChange> Pending)
{
    public static FavouriteGamesState Empty { get; } = new([], []);
}

/// <summary>
/// Per-GrevID favourite games. Works offline: every change applies locally at once and, for a
/// linked profile, is queued until grev.dad accepts it. After the queue is replayed grev.dad's list
/// becomes the local list, so a game starred on the website shows up here too.
/// </summary>
public sealed class FavouriteGameService
{
    public const int MaxFavourites = 50;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = JsonDefaults.IndentedWithStringEnums;

    public FavouriteGameService(AppPaths paths) => _paths = paths;

    /// <summary>Library games use a key that is stable on this PC and readable on grev.dad.</summary>
    public static string KeyForLibraryGame(string gameId) => $"grevhome:{gameId}";

    public async Task<FavouriteGamesState> GetAsync(string grevId, CancellationToken cancellationToken = default)
    {
        var path = FilePath(grevId);
        if (!File.Exists(path)) return FavouriteGamesState.Empty;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<FavouriteGamesState>(stream, _json, cancellationToken) ?? FavouriteGamesState.Empty;
        }
        catch (JsonException)
        {
            CorruptDataQuarantine.TryPreserve(_paths, path, "FavouriteGames", "Favourite games JSON could not be parsed.", out _);
            return FavouriteGamesState.Empty;
        }
    }

    public async Task<bool> IsFavouriteAsync(string grevId, string itemKey, CancellationToken cancellationToken = default) =>
        (await GetAsync(grevId, cancellationToken)).Games.Any(game => KeysMatch(game.ItemKey, itemKey));

    /// <summary>Stars or unstars a game. Returns whether it is now a favourite. queueForSync is
    /// true for a linked profile so the change reaches grev.dad later.</summary>
    public async Task<bool> ToggleAsync(string grevId, FavouriteGame game, bool queueForSync, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await GetAsync(grevId, cancellationToken);
            var exists = state.Games.Any(item => KeysMatch(item.ItemKey, game.ItemKey));
            if (!exists && state.Games.Count >= MaxFavourites)
                throw new InvalidOperationException($"You can favourite up to {MaxFavourites} games.");
            var games = exists
                ? state.Games.Where(item => !KeysMatch(item.ItemKey, game.ItemKey)).ToArray()
                : state.Games.Append(game with { AddedAtUtc = game.AddedAtUtc ?? DateTimeOffset.UtcNow }).ToArray();
            var pending = state.Pending.Where(change => !KeysMatch(change.Game.ItemKey, game.ItemKey)).ToList();
            if (queueForSync) pending.Add(new FavouriteChange(exists ? FavouriteChangeKind.Remove : FavouriteChangeKind.Add, game));
            await WriteAsync(grevId, new FavouriteGamesState(games, pending), cancellationToken);
            return !exists;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>After the queue has been sent: grev.dad's list becomes the local list, keeping any
    /// change made while the sync was running.</summary>
    public async Task ApplyRemoteAsync(
        string grevId, IReadOnlyList<FavouriteGame> remote, IReadOnlyList<FavouriteChange> sent,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await GetAsync(grevId, cancellationToken);
            var stillPending = state.Pending.Where(change => !sent.Contains(change)).ToArray();
            var games = remote.ToList();
            foreach (var change in stillPending)
            {
                games.RemoveAll(game => KeysMatch(game.ItemKey, change.Game.ItemKey));
                if (change.Kind == FavouriteChangeKind.Add) games.Add(change.Game);
            }
            await WriteAsync(grevId, new FavouriteGamesState(games, stillPending), cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static bool KeysMatch(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private async Task WriteAsync(string grevId, FavouriteGamesState state, CancellationToken cancellationToken)
    {
        var path = FilePath(grevId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, state, _json, cancellationToken);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string FilePath(string grevId) =>
        Path.Combine(_paths.GetProfilePresentation(grevId), "Favourites", "favourite-games.json");
}
