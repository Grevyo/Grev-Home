namespace GrevHome.Presentation;

/// <summary>Packaged transparent PNG branding available before an app is installed.</summary>
public static class PackageBrandingAssets
{
    private const string Root = "pack://application:,,,/Assets/Apps/";

    public const string RetroArch = Root + "RetroArch/icon.png";
    public const string PCSX2 = Root + "PCSX2/icon.png";
    public const string Steam = Root + "Steam/icon.png";
    public const string Discord = Root + "Discord/icon.png";
    public const string GrevReader = Root + "GrevReader/icon.png";
    public const string Dolphin = Root + "Dolphin/icon.png";
    public const string Azahar = Root + "Azahar/icon.png";
    public const string RPCS3 = Root + "RPCS3/icon.png";
    public const string Cemu = Root + "Cemu/icon.png";
    public const string Xenia = Root + "Xenia/icon.png";
    public const string Xemu = Root + "Xemu/icon.png";
    public const string Vita3K = Root + "Vita3K/icon.png";
    public const string Kodi = Root + "Kodi/icon.png";
    public const string Plex = Root + "Plex/icon.png";
    public const string Jellyfin = Root + "Jellyfin/icon.png";
    public const string Stremio = Root + "Stremio/icon.png";

    public static string ForApp(string appId) => appId.ToLowerInvariant() switch
    {
        "retroarch" => RetroArch,
        "pcsx2" => PCSX2,
        "steam" => Steam,
        "discord" => Discord,
        "grevreader" => GrevReader,
        "dolphin" => Dolphin,
        "azahar" => Azahar,
        "rpcs3" => RPCS3,
        "cemu" => Cemu,
        "xenia" => Xenia,
        "xemu" => Xemu,
        "vita3k" => Vita3K,
        "kodi" => Kodi,
        "plex-htpc" => Plex,
        "jellyfin-media-player" => Jellyfin,
        "stremio" => Stremio,
        _ => throw new ArgumentOutOfRangeException(nameof(appId), appId, "No packaged PNG branding exists for this app.")
    };
}
