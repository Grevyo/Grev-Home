using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrevHome.Games;

public sealed record GameArtworkSearchResult(
    string Title,
    string Provider,
    string ArtworkUrl,
    int MatchScore,
    string? Summary = null);

/// <summary>
/// Best-effort box art for scanned games. Libretro is the primary catalogue and Wikipedia supplies
/// search results and coverage for systems such as PS2. Neither source needs an account or API key.
///
/// This is deliberately best-effort. Matching is by title, so a ROM named differently to the
/// libretro set simply returns nothing, and every failure (no match, offline, timeout, oversized
/// response) is swallowed and treated as "no art". Art is a bonus on top of a scan, never a reason
/// for one to fail.
/// </summary>
public sealed class GameBoxArtService : IDisposable
{
    private const string BaseUrl = "https://raw.githubusercontent.com/libretro-thumbnails/";
    private const long MaxImageBytes = 12L * 1024L * 1024L;

    private readonly HttpClient _http;
    private readonly Dictionary<GamePlatform, IReadOnlyList<GameArtworkSearchResult>> _catalogCache = new();
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private bool _disposed;

    public GameBoxArtService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("GrevHome/0.13");
        }
    }

    public static bool IsSupported(GamePlatform platform) => Enum.IsDefined(platform);

    /// <summary>
    /// Searches the public Libretro thumbnail catalogue. This is used by both the explicit automatic
    /// scrape and the manual result picker; importing a game never depends on this call succeeding.
    /// </summary>
    public async Task<IReadOnlyList<GameArtworkSearchResult>> SearchAsync(
        GamePlatform platform,
        string query,
        int maximumResults = 18,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GameArtworkSearchResult>();
        }

        var normalizedQuery = NormalizeForMatch(query);
        var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = new List<GameArtworkSearchResult>();
        if (GetThumbnailSystem(platform) is { } system)
        {
            var catalogue = await GetCatalogueAsync(platform, system, cancellationToken);
            matches.AddRange(catalogue
            .Select(result => result with { MatchScore = Score(NormalizeForMatch(result.Title), normalizedQuery, queryTokens) })
            .Where(result => result.MatchScore > 0));
        }

        matches.AddRange(await SearchWikipediaAsync(query, normalizedQuery, queryTokens, cancellationToken));
        return matches
            .GroupBy(result => (result.Provider, result.Title), StringTupleComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(result => result.MatchScore).First())
            .OrderByDescending(result => result.MatchScore)
            .ThenBy(result => result.Provider == "Libretro" ? 0 : 1)
            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maximumResults, 1, 40))
            .ToArray();
    }

    public async Task<string?> TryDownloadResultAsync(
        GameArtworkSearchResult result,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || !Uri.TryCreate(result.ArtworkUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(uri.Host, "upload.wikimedia.org", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return await TryDownloadUriAsync(uri, destinationDirectory, cancellationToken);
    }

    private async Task<IReadOnlyList<GameArtworkSearchResult>> SearchWikipediaAsync(
        string query,
        string normalizedQuery,
        IReadOnlyList<string> queryTokens,
        CancellationToken cancellationToken)
    {
        try
        {
            var search = Uri.EscapeDataString($"{query} video game");
            var url = new Uri("https://en.wikipedia.org/w/api.php?action=query&format=json&generator=search" +
                              $"&gsrsearch={search}&gsrlimit=12&prop=pageimages%7Cextracts&pithumbsize=600&exintro=1&explaintext=1&exsentences=2");
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<GameArtworkSearchResult>();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("query", out var queryNode) ||
                !queryNode.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<GameArtworkSearchResult>();
            }

            var results = new List<GameArtworkSearchResult>();
            foreach (var page in pages.EnumerateObject().Select(property => property.Value))
            {
                if (!page.TryGetProperty("title", out var titleNode) ||
                    !page.TryGetProperty("thumbnail", out var thumbnail) ||
                    !thumbnail.TryGetProperty("source", out var sourceNode)) continue;
                var title = titleNode.GetString();
                var source = sourceNode.GetString();
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(source)) continue;
                var summary = page.TryGetProperty("extract", out var extractNode) ? extractNode.GetString() : null;
                var score = Score(NormalizeForMatch(title.Replace("(video game)", string.Empty, StringComparison.OrdinalIgnoreCase)), normalizedQuery, queryTokens);
                if (score > 0) results.Add(new GameArtworkSearchResult(title, "Wikipedia", source, Math.Max(1, score - 40), summary));
            }
            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or JsonException or InvalidOperationException)
        {
            return Array.Empty<GameArtworkSearchResult>();
        }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Provider, string Title)>
    {
        public static StringTupleComparer OrdinalIgnoreCase { get; } = new();
        public bool Equals((string Provider, string Title) x, (string Provider, string Title) y) =>
            string.Equals(x.Provider, y.Provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Title, y.Title, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Provider, string Title) value) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Provider),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Title));
    }

    /// <summary>
    /// Downloads box art for a game into <paramref name="destinationDirectory"/> and returns the
    /// file path, or null when no art could be matched. Never throws for an expected failure.
    /// </summary>
    public async Task<string?> TryDownloadBoxArtAsync(
        GamePlatform platform,
        string fileTitle,
        string cleanedTitle,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || GetThumbnailSystem(platform) is not { } system)
        {
            return null;
        }

        // The raw filename usually already matches the libretro naming set (which follows the same
        // No-Intro conventions ROM sets use), so try it before the tidied display name.
        foreach (var title in new[] { fileTitle, cleanedTitle }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = await TryDownloadAsync(system, title, destinationDirectory, cancellationToken);
            if (path is not null)
            {
                return path;
            }
        }

        return null;
    }

    private async Task<string?> TryDownloadAsync(
        string system,
        string title,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await TryDownloadUriAsync(BuildUrl(system, title), destinationDirectory, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException
                                       or UnauthorizedAccessException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<string?> TryDownloadUriAsync(Uri url, string destinationDirectory, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            if (response.Content.Headers.ContentLength is > MaxImageBytes)
            {
                return null;
            }

            Directory.CreateDirectory(destinationDirectory);
            var target = Path.Combine(destinationDirectory, $"boxart-{Guid.NewGuid():N}.png");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = File.Create(target))
            {
                await source.CopyToAsync(file, cancellationToken);
            }

            var written = new FileInfo(target);
            if (!written.Exists || written.Length == 0 || written.Length > MaxImageBytes)
            {
                TryDelete(target);
                return null;
            }

            return target;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException
                                       or UnauthorizedAccessException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<GameArtworkSearchResult>> GetCatalogueAsync(
        GamePlatform platform,
        string system,
        CancellationToken cancellationToken)
    {
        await _catalogGate.WaitAsync(cancellationToken);
        try
        {
            if (_catalogCache.TryGetValue(platform, out var cached)) return cached;

            var url = new Uri($"https://api.github.com/repos/libretro-thumbnails/{system}/git/trees/master?recursive=1");
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<GameArtworkSearchResult>();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("tree", out var tree) || tree.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<GameArtworkSearchResult>();
            }

            var results = new List<GameArtworkSearchResult>();
            foreach (var item in tree.EnumerateArray())
            {
                if (!item.TryGetProperty("path", out var pathElement)) continue;
                var path = pathElement.GetString();
                if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Named_Boxarts/", StringComparison.Ordinal) ||
                    !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

                var title = Path.GetFileNameWithoutExtension(path);
                var artworkUrl = $"{BaseUrl}{system}/master/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}";
                results.Add(new GameArtworkSearchResult(title, "Libretro", artworkUrl, 0));
            }

            var immutable = (IReadOnlyList<GameArtworkSearchResult>)results.ToArray();
            _catalogCache[platform] = immutable;
            return immutable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException
                                       or JsonException or InvalidOperationException)
        {
            return Array.Empty<GameArtworkSearchResult>();
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    private static readonly Regex MatchNoise = new(@"[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string NormalizeForMatch(string value) =>
        MatchNoise.Replace(value.ToLowerInvariant(), " ").Trim();

    private static int Score(string candidate, string query, IReadOnlyList<string> queryTokens)
    {
        if (candidate == query) return 1000;
        if (candidate.StartsWith(query, StringComparison.Ordinal)) return 850 - Math.Min(200, candidate.Length - query.Length);
        if (candidate.Contains(query, StringComparison.Ordinal)) return 700 - Math.Min(200, candidate.Length - query.Length);

        var candidateTokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var matched = queryTokens.Count(candidateTokens.Contains);
        if (matched == 0) return 0;
        return (int)Math.Round(500d * matched / Math.Max(1, queryTokens.Count)) - Math.Min(100, Math.Abs(candidate.Length - query.Length));
    }

    private static Uri BuildUrl(string system, string title)
    {
        // libretro's rule: characters that are illegal in filenames are stored as underscores.
        var escaped = new string(title.Select(character => "&*/:`<>?\\|\"".Contains(character) ? '_' : character).ToArray());
        return new Uri($"{BaseUrl}{system}/master/Named_Boxarts/{Uri.EscapeDataString(escaped)}.png", UriKind.Absolute);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp image is harmless; it lives under the profile's own scan staging area.
        }
    }

    /// <summary>
    /// Grev Home console to libretro thumbnail repository. A wrong or missing entry only ever costs
    /// the artwork for that console - the request 404s and the scan carries on unaffected.
    /// </summary>
    private static string? GetThumbnailSystem(GamePlatform platform) => platform switch
    {
        GamePlatform.NintendoEntertainmentSystem => "Nintendo_-_Nintendo_Entertainment_System",
        GamePlatform.SuperNintendo => "Nintendo_-_Super_Nintendo_Entertainment_System",
        GamePlatform.Nintendo64 => "Nintendo_-_Nintendo_64",
        GamePlatform.GameBoy => "Nintendo_-_Game_Boy",
        GamePlatform.GameBoyColor => "Nintendo_-_Game_Boy_Color",
        GamePlatform.GameBoyAdvance => "Nintendo_-_Game_Boy_Advance",
        GamePlatform.NintendoDS => "Nintendo_-_Nintendo_DS",
        GamePlatform.Nintendo3DS => "Nintendo_-_Nintendo_3DS",
        GamePlatform.GameCube => "Nintendo_-_GameCube",
        GamePlatform.Wii => "Nintendo_-_Wii",
        GamePlatform.SegaMasterSystem => "Sega_-_Master_System_-_Mark_III",
        GamePlatform.SegaGenesis => "Sega_-_Mega_Drive_-_Genesis",
        GamePlatform.SegaGameGear => "Sega_-_Game_Gear",
        GamePlatform.SegaCD => "Sega_-_Mega-CD_-_Sega_CD",
        GamePlatform.Sega32X => "Sega_-_32X",
        GamePlatform.SegaSaturn => "Sega_-_Saturn",
        GamePlatform.SegaDreamcast => "Sega_-_Dreamcast",
        GamePlatform.PlayStation => "Sony_-_PlayStation",
        GamePlatform.PlayStationPortable => "Sony_-_PlayStation_Portable",
        GamePlatform.PcEngine => "NEC_-_PC_Engine_-_TurboGrafx_16",
        GamePlatform.PcEngineCD => "NEC_-_PC_Engine_CD_-_TurboGrafx-CD",
        GamePlatform.NeoGeoPocket => "SNK_-_Neo_Geo_Pocket_Color",
        GamePlatform.WonderSwan => "Bandai_-_WonderSwan_Color",
        GamePlatform.Atari2600 => "Atari_-_2600",
        GamePlatform.Atari5200 => "Atari_-_5200",
        GamePlatform.Atari7800 => "Atari_-_7800",
        GamePlatform.AtariJaguar => "Atari_-_Jaguar",
        GamePlatform.AtariLynx => "Atari_-_Lynx",
        GamePlatform.Commodore64 => "Commodore_-_64",
        GamePlatform.CommodoreAmiga => "Commodore_-_Amiga",
        GamePlatform.Arcade => "MAME",

        // PlayStation 2 is emulated by PCSX2 rather than a libretro core and has no thumbnail set.
        GamePlatform.PlayStation2 => null,
        _ => null
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _catalogGate.Dispose();
        _http.Dispose();
    }
}
