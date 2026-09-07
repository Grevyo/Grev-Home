namespace GrevHome.Games;

/// <summary>
/// Best-effort box art for scanned games, fetched from libretro's public thumbnail repositories -
/// the same artwork RetroArch itself downloads. No account or API key is involved: each image is a
/// plain public file request.
///
/// This is deliberately best-effort. Matching is by title, so a ROM named differently to the
/// libretro set simply returns nothing, and every failure (no match, offline, timeout, oversized
/// response) is swallowed and treated as "no art". Art is a bonus on top of a scan, never a reason
/// for one to fail. PlayStation 2 has no libretro thumbnail set, so PS2 games never get art here.
/// </summary>
public sealed class GameBoxArtService : IDisposable
{
    private const string BaseUrl = "https://raw.githubusercontent.com/libretro-thumbnails/";
    private const long MaxImageBytes = 12L * 1024L * 1024L;

    private readonly HttpClient _http;
    private bool _disposed;

    public GameBoxArtService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("GrevHome/0.13");
        }
    }

    public static bool IsSupported(GamePlatform platform) => GetThumbnailSystem(platform) is not null;

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
            var url = BuildUrl(system, title);
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
        _http.Dispose();
    }
}
