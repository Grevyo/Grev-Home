using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using GrevHome.Profiles;
using GrevHome.Storage;

namespace GrevHome.Online;

/// <summary>Which way one identity field should move in a sync.</summary>
public enum IdentitySyncDirection { None, Push, Pull }

/// <summary>Fingerprints of each identity field the last time both sides agreed.</summary>
public sealed record IdentitySyncBaseline(
    IReadOnlyDictionary<string, string> Local,
    IReadOnlyDictionary<string, string> Remote);

/// <summary>
/// Keeps a linked profile's identity (display name, bio, avatar, banner) the same in Grev Home and
/// on grev.dad. It is a three-way sync per field: each side's current fingerprint is compared with
/// the fingerprint stored when the two last agreed, so whichever side changed is copied to the
/// other. The first sync after linking takes grev.dad's value for any field grev.dad has, which is
/// how a grev.dad profile comes over to a new Grev Home install. If both sides changed the same
/// field since the last sync, this PC's edit wins, since it is the newer deliberate change here.
/// </summary>
public sealed class GrevDadIdentitySyncService
{
    public static readonly string[] Fields = ["displayName", "bio", "avatar", "banner"];

    private readonly AppPaths _paths;
    private readonly ProfileService _profiles;
    private readonly ProfilePresentationSettingsService _presentation;
    private readonly JsonSerializerOptions _json = JsonDefaults.IndentedWithStringEnums;

    public GrevDadIdentitySyncService(AppPaths paths, ProfileService profiles)
    {
        _paths = paths;
        _profiles = profiles;
        _presentation = new ProfilePresentationSettingsService(paths);
    }

    /// <summary>Pure decision for one field; see the class summary.</summary>
    public static IdentitySyncDirection Decide(string? localNow, string? remoteNow, string? localBase, string? remoteBase, bool remoteHasValue, bool localHasValue)
    {
        if (localBase is null || remoteBase is null)
        {
            if (localNow == localBase && remoteNow == remoteBase) return IdentitySyncDirection.None;
            return remoteHasValue ? IdentitySyncDirection.Pull : localHasValue ? IdentitySyncDirection.Push : IdentitySyncDirection.None;
        }
        var localChanged = localNow != localBase;
        var remoteChanged = remoteNow != remoteBase;
        if (localChanged) return IdentitySyncDirection.Push;
        return remoteChanged ? IdentitySyncDirection.Pull : IdentitySyncDirection.None;
    }

    /// <summary>Syncs and returns grev.dad's profile document afterwards (null for a profile this
    /// sync does not apply to). Throws on network failure; callers treat that as "try later".</summary>
    public async Task<GrevDadProfileDocument?> SyncAsync(
        GrevDadAccountService accounts, string grevId, CancellationToken cancellationToken = default)
    {
        var profile = (await _profiles.GetProfilesAsync(cancellationToken))
            .FirstOrDefault(item => string.Equals(item.GrevId, grevId, StringComparison.OrdinalIgnoreCase));
        if (profile is null || profile.IsBuiltInGuest) return null;

        var document = await accounts.GetProfileDocumentAsync(grevId, null, cancellationToken);
        var presentation = await _presentation.GetAsync(grevId, cancellationToken);
        var baseline = await ReadBaselineAsync(grevId, cancellationToken);
        var local = LocalFingerprints(profile, presentation);
        var remote = RemoteFingerprints(document);

        var push = new Dictionary<string, object?>();
        var pull = new HashSet<string>();
        foreach (var field in Fields)
        {
            var direction = Decide(
                local[field], remote[field],
                baseline?.Local.GetValueOrDefault(field), baseline?.Remote.GetValueOrDefault(field),
                remoteHasValue: RemoteHasValue(document, field),
                localHasValue: LocalHasValue(profile, presentation, field));
            if (direction == IdentitySyncDirection.Pull) pull.Add(field);
            else if (direction == IdentitySyncDirection.Push) AddPush(push, field, profile, presentation, grevId);
        }

        if (push.Count > 0) document = await accounts.UpdateIdentityAsync(grevId, push, cancellationToken);
        if (pull.Count > 0)
        {
            (profile, presentation) = await PullAsync(grevId, profile, presentation, document, pull, cancellationToken);
        }

        await WriteBaselineAsync(grevId, new IdentitySyncBaseline(LocalFingerprints(profile, presentation), RemoteFingerprints(document)), cancellationToken);
        return document;
    }

    private void AddPush(Dictionary<string, object?> push, string field, LocalProfile profile, ProfilePresentationSettings presentation, string grevId)
    {
        switch (field)
        {
            case "displayName": push["displayName"] = profile.DisplayName; break;
            case "bio": push["bio"] = string.IsNullOrWhiteSpace(profile.Bio) ? null : profile.Bio; break;
            case "avatar":
                push["avatarMedia"] = IsCustomAvatar(profile) ? ProfileMediaDataUrl.TryRead(_paths, grevId, profile.AvatarImageFile) : null;
                break;
            case "banner":
                push["coverMedia"] = IsCustomBanner(presentation) ? ProfileMediaDataUrl.TryRead(_paths, grevId, presentation.BannerImageFile) : null;
                break;
        }
    }

    private async Task<(LocalProfile, ProfilePresentationSettings)> PullAsync(
        string grevId, LocalProfile profile, ProfilePresentationSettings presentation, GrevDadProfileDocument document,
        IReadOnlySet<string> fields, CancellationToken cancellationToken)
    {
        var card = document.Card;
        var temporaryFiles = new List<string>();
        try
        {
            if (fields.Contains("displayName") || fields.Contains("bio") || fields.Contains("avatar"))
            {
                var displayName = fields.Contains("displayName") && !string.IsNullOrWhiteSpace(card.DisplayName)
                    ? Truncate(card.DisplayName, ProfileService.MaxDisplayNameLength)
                    : profile.DisplayName;
                var bio = fields.Contains("bio") ? Truncate(card.Bio ?? "", ProfileService.MaxBioLength) : null;
                var avatarKey = profile.AvatarKey;
                string? avatarPath = null;
                if (fields.Contains("avatar"))
                {
                    avatarPath = card.AvatarMedia is null ? null : WriteDataUrlAsPng(card.AvatarMedia, temporaryFiles);
                    avatarKey = avatarPath is not null
                        ? ProfileAvatarCatalog.CustomKey
                        : card.AvatarMedia is null && IsCustomAvatar(profile) ? ProfileAvatarCatalog.DefaultKey : profile.AvatarKey;
                }
                profile = await _profiles.UpdateProfileAsync(grevId, displayName, avatarKey, null, avatarPath, bio, null, cancellationToken);
            }

            if (fields.Contains("banner"))
            {
                var bannerPath = card.CoverMedia is null ? null : WriteDataUrlAsPng(card.CoverMedia, temporaryFiles);
                var bannerKey = bannerPath is not null
                    ? ProfileBannerCatalog.CustomKey
                    : card.CoverMedia is null && IsCustomBanner(presentation) ? ProfileBannerCatalog.DefaultKey : presentation.BannerKey;
                presentation = await _presentation.SaveAsync(
                    grevId, bannerKey, presentation.ShowcaseMode, bannerPath, presentation.CardFrame, presentation.AvatarShape,
                    presentation.ShowUsername, presentation.ShowLevel, presentation.ShowXp, presentation.ShowPlaytime,
                    presentation.ShowSessions, presentation.ShowStatus, cancellationToken);
            }
        }
        finally
        {
            foreach (var file in temporaryFiles)
            {
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        return (profile, presentation);
    }

    private static string Truncate(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    /// <summary>Decodes any picture grev.dad accepts (PNG, JPEG, GIF, WebP) and re-encodes it as a
    /// PNG, which is what Grev Home's avatar and banner importers take.</summary>
    private string? WriteDataUrlAsPng(string dataUrl, List<string> temporaryFiles)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0) return null;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]); }
        catch (FormatException) { return null; }
        try
        {
            using var input = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
            var path = Path.Combine(Path.GetTempPath(), $"grev-home-identity-{Guid.NewGuid():N}.png");
            using (var output = File.Create(path)) encoder.Save(output);
            temporaryFiles.Add(path);
            return path;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException)
        {
            // A picture this PC cannot decode (for example WebP without the Windows codec) is
            // skipped rather than failing the whole sync; the baseline still records grev.dad's
            // value so it is not retried every time.
            return null;
        }
    }

    private static bool IsCustomAvatar(LocalProfile profile) =>
        string.Equals(profile.AvatarKey, ProfileAvatarCatalog.CustomKey, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(profile.AvatarImageFile);

    private static bool IsCustomBanner(ProfilePresentationSettings presentation) =>
        string.Equals(presentation.BannerKey, ProfileBannerCatalog.CustomKey, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(presentation.BannerImageFile);

    private static bool RemoteHasValue(GrevDadProfileDocument document, string field) => field switch
    {
        "displayName" => !string.IsNullOrWhiteSpace(document.Card.DisplayName),
        "bio" => !string.IsNullOrWhiteSpace(document.Card.Bio),
        "avatar" => !string.IsNullOrWhiteSpace(document.Card.AvatarMedia),
        _ => !string.IsNullOrWhiteSpace(document.Card.CoverMedia)
    };

    private static bool LocalHasValue(LocalProfile profile, ProfilePresentationSettings presentation, string field) => field switch
    {
        "displayName" => true,
        "bio" => !string.IsNullOrWhiteSpace(profile.Bio),
        "avatar" => IsCustomAvatar(profile),
        _ => IsCustomBanner(presentation)
    };

    private Dictionary<string, string> LocalFingerprints(LocalProfile profile, ProfilePresentationSettings presentation) => new()
    {
        ["displayName"] = Hash(profile.DisplayName),
        ["bio"] = Hash(profile.Bio ?? ""),
        ["avatar"] = IsCustomAvatar(profile) ? FileHash(Path.Combine(_paths.GetProfileRoot(profile.GrevId), Path.GetFileName(profile.AvatarImageFile!))) : "",
        ["banner"] = IsCustomBanner(presentation) ? FileHash(Path.Combine(_paths.GetProfileRoot(profile.GrevId), Path.GetFileName(presentation.BannerImageFile!))) : ""
    };

    private static Dictionary<string, string> RemoteFingerprints(GrevDadProfileDocument document) => new()
    {
        ["displayName"] = Hash(document.Card.DisplayName),
        ["bio"] = Hash(document.Card.Bio ?? ""),
        ["avatar"] = string.IsNullOrEmpty(document.Card.AvatarMedia) ? "" : Hash(document.Card.AvatarMedia),
        ["banner"] = string.IsNullOrEmpty(document.Card.CoverMedia) ? "" : Hash(document.Card.CoverMedia)
    };

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string FileHash(string path)
    {
        try { return File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : ""; }
        catch (IOException) { return ""; }
    }

    private string BaselinePath(string grevId) => Path.Combine(_paths.GetProfilePresentation(grevId), "GrevDad", "identity-sync.json");

    private async Task<IdentitySyncBaseline?> ReadBaselineAsync(string grevId, CancellationToken cancellationToken)
    {
        var path = BaselinePath(grevId);
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<IdentitySyncBaseline>(stream, _json, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WriteBaselineAsync(string grevId, IdentitySyncBaseline baseline, CancellationToken cancellationToken)
    {
        var path = BaselinePath(grevId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, baseline, _json, cancellationToken);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
