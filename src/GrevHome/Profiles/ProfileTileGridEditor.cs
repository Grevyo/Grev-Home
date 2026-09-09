using GrevHome.Input;

namespace GrevHome.Profiles;

/// <summary>
/// Controller-first replacement for grev.dad's mouse-drag tile editor. A gamepad has no pointer,
/// so instead of drag-and-drop this drives a grid cursor: D-Pad/Left-Stick moves the cursor one
/// cell at a time (already surfaced as InputAction.Up/Down/Left/Right - see
/// MainWindow.AppControllerRuntime.cs), Accept (A) either picks up the tile under the cursor or
/// drops a held tile back down, and Back (B) cancels a pick-up and restores the tile's original
/// position. This is UI-agnostic on purpose: a XAML view renders whatever this reports and forwards
/// its own InputAction stream into HandleInput, the same way DashboardView already reacts to focus
/// movement today.
///
/// The same cursor/pick-up/drop model is intended to move to grev.dad's web tile editor too (driven
/// by keyboard arrows there, and by the Gamepad API when browsing from Grev Home's in-app browser),
/// so a profile's tile layout behaves identically however it was last edited.
/// </summary>
public enum ProfileTileEditorMode
{
    Browsing,
    Holding,
    Resizing
}

public sealed class ProfileTileGridEditor
{
    private List<ProfileTile> _tiles;

    public ProfileTileGridEditor(IReadOnlyList<ProfileTile> tiles)
    {
        _tiles = tiles.ToList();
        var first = _tiles.FirstOrDefault();
        CursorX = first?.X ?? 0;
        CursorY = first?.Y ?? 0;
    }

    public IReadOnlyList<ProfileTile> Tiles => _tiles;
    public ProfileTileEditorMode Mode { get; private set; } = ProfileTileEditorMode.Browsing;
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }

    /// <summary>The tile currently picked up or being resized, or null while just browsing.</summary>
    public ProfileTile? ActiveTile { get; private set; }
    private ProfileTile? _activeTileOrigin;

    public ProfileTile? TileAt(int x, int y) =>
        _tiles.FirstOrDefault(tile => x >= tile.X && x < tile.X + tile.Width && y >= tile.Y && y < tile.Y + tile.Height);

    /// <summary>Feed one InputAction from the controller (or an equivalent keyboard binding) in.
    /// Returns true if the input was consumed by the editor and should not also move page focus.</summary>
    public bool HandleInput(InputAction action) => Mode switch
    {
        ProfileTileEditorMode.Browsing => HandleBrowsing(action),
        ProfileTileEditorMode.Holding => HandleHolding(action),
        ProfileTileEditorMode.Resizing => HandleResizing(action),
        _ => false
    };

    private bool HandleBrowsing(InputAction action)
    {
        switch (action)
        {
            case InputAction.Up or InputAction.Down or InputAction.Left or InputAction.Right:
                MoveCursor(action);
                return true;
            case InputAction.Accept:
                var target = TileAt(CursorX, CursorY);
                if (target is null) return false; // let empty-cell Accept fall through to "add tile" UI
                ActiveTile = target;
                _activeTileOrigin = target;
                Mode = ProfileTileEditorMode.Holding;
                return true;
            default:
                return false;
        }
    }

    private bool HandleHolding(InputAction action)
    {
        if (ActiveTile is null) { Mode = ProfileTileEditorMode.Browsing; return false; }
        switch (action)
        {
            case InputAction.Up or InputAction.Down or InputAction.Left or InputAction.Right:
                TryMoveHeldTile(action);
                return true;
            case InputAction.Accept:
                // Dropping is only ever onto an already-valid position (TryMoveHeldTile never
                // commits an overlapping/out-of-bounds move), so Accept just confirms it.
                ActiveTile = null;
                _activeTileOrigin = null;
                Mode = ProfileTileEditorMode.Browsing;
                return true;
            case InputAction.Back:
                RevertHeldTile();
                ActiveTile = null;
                _activeTileOrigin = null;
                Mode = ProfileTileEditorMode.Browsing;
                return true;
            default:
                return false;
        }
    }

    private bool HandleResizing(InputAction action)
    {
        if (ActiveTile is null) { Mode = ProfileTileEditorMode.Browsing; return false; }
        switch (action)
        {
            case InputAction.Up: return TryResize(heightDelta: -1);
            case InputAction.Down: return TryResize(heightDelta: +1);
            case InputAction.Left: return TryResize(widthDelta: -1);
            case InputAction.Right: return TryResize(widthDelta: +1);
            case InputAction.Accept:
                ActiveTile = null;
                _activeTileOrigin = null;
                Mode = ProfileTileEditorMode.Browsing;
                return true;
            case InputAction.Back:
                RevertHeldTile();
                ActiveTile = null;
                _activeTileOrigin = null;
                Mode = ProfileTileEditorMode.Browsing;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Switches the currently-held tile into resize mode. Only valid while Holding.</summary>
    public bool BeginResize()
    {
        if (Mode != ProfileTileEditorMode.Holding || ActiveTile is null) return false;
        Mode = ProfileTileEditorMode.Resizing;
        return true;
    }

    private void MoveCursor(InputAction direction)
    {
        var (dx, dy) = Delta(direction);
        CursorX = Math.Clamp(CursorX + dx, 0, ProfileTileGrid.Columns - 1);
        CursorY = Math.Clamp(CursorY + dy, 0, ProfileTileGrid.MaxGridY);
    }

    private void TryMoveHeldTile(InputAction direction)
    {
        if (ActiveTile is null) return;
        var (dx, dy) = Delta(direction);
        var candidate = ActiveTile with { X = ActiveTile.X + dx, Y = ActiveTile.Y + dy };
        if (!ProfileTileGrid.InBounds(candidate)) return;
        if (_tiles.Any(other => other.TileId != candidate.TileId && ProfileTileGrid.Overlaps(candidate, other))) return;

        ReplaceActiveTile(candidate);
        CursorX = candidate.X;
        CursorY = candidate.Y;
    }

    private bool TryResize(int widthDelta = 0, int heightDelta = 0)
    {
        if (ActiveTile is null) return false;
        var candidate = ActiveTile with
        {
            Width = Math.Clamp(ActiveTile.Width + widthDelta, ProfileTileGrid.MinWidth, ProfileTileGrid.MaxWidth),
            Height = Math.Clamp(ActiveTile.Height + heightDelta, ProfileTileGrid.MinHeight, ProfileTileGrid.MaxHeight)
        };
        if (candidate == ActiveTile) return true; // clamped to a no-op edge; still consume the input
        if (!ProfileTileGrid.InBounds(candidate)) return true;
        if (_tiles.Any(other => other.TileId != candidate.TileId && ProfileTileGrid.Overlaps(candidate, other))) return true;

        ReplaceActiveTile(candidate);
        return true;
    }

    private void RevertHeldTile()
    {
        if (_activeTileOrigin is null) return;
        ReplaceActiveTile(_activeTileOrigin);
    }

    private void ReplaceActiveTile(ProfileTile updated)
    {
        var index = _tiles.FindIndex(tile => tile.TileId == ActiveTile!.TileId);
        if (index >= 0) _tiles[index] = updated;
        ActiveTile = updated;
    }

    private static (int Dx, int Dy) Delta(InputAction direction) => direction switch
    {
        InputAction.Up => (0, -1),
        InputAction.Down => (0, 1),
        InputAction.Left => (-1, 0),
        InputAction.Right => (1, 0),
        _ => (0, 0)
    };
}
