using System.IO;

namespace GrevHome.Online;

public sealed partial class GrevDadCoordinator
{
    /// <summary>Synchronises the linked Grev.dad profile tile layout now. Returns false when the
    /// profile is not linked or the remote sync is temporarily unavailable; local tile data is
    /// left untouched so the next open/save/session sync can retry safely.</summary>
    public async Task<bool> SyncProfileTilesNowAsync(string grevId, CancellationToken cancellationToken = default)
    {
        var sync = _grevDadProfileSync;
        if (sync is null || string.IsNullOrWhiteSpace(grevId)) return false;
        try
        {
            await sync.SyncProfileTilesAsync(grevId, cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsExpectedGrevDadBackgroundFailure(ex) ||
                                   ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            return false;
        }
    }
}
