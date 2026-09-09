using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GrevHome.Input;
using GrevHome.Profiles;

namespace GrevHome.Views;

/// <summary>
/// Controller-first profile tile editor. Renders GrevHome.Profiles.ProfileTileGridEditor's cursor
/// state onto a scrollable grid; every actual placement/collision/resize decision lives in that
/// UI-agnostic class (see docs/PROFILE_TILES.md) - this view only draws it and turns input into
/// InputAction calls, the same separation DashboardView already has from
/// DashboardTilePresentationService.
///
/// NOT VERIFIED: authored without a Windows/.NET toolchain (see the repository-wide caveat in
/// docs/PROFILE_TILES.md). XAML/WPF mistakes - a bad binding, a missing using, a XAML syntax slip -
/// are exactly the kind of error this environment cannot catch; a Windows build is required before
/// trusting this file.
/// </summary>
public partial class ProfileTileEditorView : UserControl
{
    private const int CellSize = 56;
    private const int CellGap = 4;

    private ProfileTileGridEditor? _editor;

    public event EventHandler? BackRequested;
    public event Action<IReadOnlyList<ProfileTile>>? SaveRequested;

    /// <summary>Set by the host each time it forwards an event, so the footer prompt reads "A" /
    /// "B" for a controller and "Enter" / "Esc" for a keyboard rather than always guessing one.</summary>
    public bool IsControllerActive { get; set; } = true;

    public ProfileTileEditorView()
    {
        InitializeComponent();
        KeyboardOverlay.Completed += OnKeyboardCompleted;
        Loaded += (_, _) => Focus();
    }

    public void Load(IReadOnlyList<ProfileTile> tiles)
    {
        _editor = new ProfileTileGridEditor(tiles);
        Render();
    }

    /// <summary>Feed one InputAction from the controller in. Returns true when the editor consumed
    /// it (so the host does not also move page focus with the same D-Pad press).</summary>
    public bool HandleInput(InputAction action)
    {
        if (_editor is null) return false;
        var consumed = _editor.HandleInput(action);
        if (consumed) Render();
        return consumed;
    }

    private void PreviewKeyDown_Handler(object sender, KeyEventArgs e)
    {
        var action = e.Key switch
        {
            Key.Up => InputAction.Up,
            Key.Down => InputAction.Down,
            Key.Left => InputAction.Left,
            Key.Right => InputAction.Right,
            Key.Enter or Key.Space => InputAction.Accept,
            Key.Escape => InputAction.Back,
            _ => (InputAction?)null
        };
        if (action is null) return;
        IsControllerActive = false;
        if (HandleInput(action.Value)) e.Handled = true;
    }

    private void AddTile_Click(object sender, RoutedEventArgs e)
    {
        if (_editor is null || sender is not Button { Tag: string tagKind }) return;
        if (!Enum.TryParse<ProfileTileKind>(tagKind, out var kind)) return;
        var (width, height) = kind switch
        {
            ProfileTileKind.Text => (3, 2),
            ProfileTileKind.Link => (2, 1),
            ProfileTileKind.Media => (3, 2),
            ProfileTileKind.Stat => (2, 1),
            _ => (2, 1)
        };
        if (_editor.AddTile(kind, width, height) is null)
        {
            PromptText.Text = "There is no free space left for a new tile.";
            return;
        }
        Render();
    }

    private void EditTile_Click(object sender, RoutedEventArgs e) => BeginEditActiveTile();

    private void BeginEditActiveTile()
    {
        if (_editor?.ActiveTile is not { } tile)
        {
            PromptText.Text = "Pick up a tile first (Accept on it), then Edit Tile.";
            return;
        }
        switch (tile.Kind)
        {
            case ProfileTileKind.Text:
                KeyboardOverlay.Open("Tile title", tile.Title, 80);
                break;
            case ProfileTileKind.Link:
                KeyboardOverlay.Open("Link URL (http:// or https://)", tile.LinkUrl, 500);
                break;
            case ProfileTileKind.Stat:
                KeyboardOverlay.Open("Stat value", tile.StatValue, 80);
                break;
            case ProfileTileKind.Media:
                PromptText.Text = "Picture tiles are edited from the media picker (not yet wired into this view).";
                break;
        }
    }

    private void OnKeyboardCompleted(string value)
    {
        if (_editor?.ActiveTile is not { } tile) return;
        var updated = tile.Kind switch
        {
            ProfileTileKind.Text => tile with { Title = value },
            ProfileTileKind.Link => tile with { LinkUrl = value },
            ProfileTileKind.Stat => tile with { StatValue = value },
            _ => tile
        };
        // ProfileTileGridEditor has no direct "replace the active tile's content" call (only
        // position/size, which is all a grid-cursor needs to move); Remove + AddTile keeps this
        // view from needing a new editor method just for text fields, at the cost of the tile
        // getting a new random TileId - acceptable here since Save always replaces the whole
        // layout, tile IDs are otherwise only meaningful within one edit session.
        _editor.RemoveActiveTile();
        var tiles = new List<ProfileTile>(_editor.Tiles) { updated with { TileId = Guid.NewGuid().ToString() } };
        _editor = new ProfileTileGridEditor(tiles);
        Render();
    }

    private void RemoveTile_Click(object sender, RoutedEventArgs e)
    {
        if (_editor?.RemoveActiveTile() != true)
        {
            PromptText.Text = "Pick up a tile first (Accept on it), then Remove Tile.";
            return;
        }
        Render();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(_editor?.Tiles ?? []);

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void Render()
    {
        GridCanvas.Children.Clear();
        if (_editor is null) return;

        GridCanvas.Width = ProfileTileGrid.Columns * CellSize;
        GridCanvas.Height = ProfileTileGrid.MaxRows * CellSize;

        foreach (var tile in _editor.Tiles)
        {
            GridCanvas.Children.Add(CreateTileElement(tile, isActive: tile.TileId == _editor.ActiveTile?.TileId));
        }
        GridCanvas.Children.Add(CreateCursorElement());

        HintText.Text = _editor.Mode switch
        {
            ProfileTileEditorMode.Holding =>
                "Move the tile, then Accept to drop it, Back to cancel, or Edit Tile / Remove Tile below.",
            ProfileTileEditorMode.Resizing => "Resize the tile. Accept confirms, Back reverts.",
            _ => "Move the cursor onto a tile and press Accept to pick it up, or add a new tile below."
        };
        PromptText.Text = BuildPrompt();

        var targetTop = (_editor.Mode == ProfileTileEditorMode.Browsing ? _editor.CursorY : (_editor.ActiveTile?.Y ?? 0)) * CellSize;
        GridScroller.ScrollToVerticalOffset(Math.Max(0, targetTop - 200));
    }

    private string BuildPrompt()
    {
        string accept, back;
        if (IsControllerActive) { accept = "A"; back = "B"; }
        else { accept = "Enter"; back = "Esc"; }
        return _editor?.Mode switch
        {
            ProfileTileEditorMode.Holding => $"{accept} Drop    {back} Cancel",
            ProfileTileEditorMode.Resizing => $"{accept} Confirm    {back} Revert",
            _ => $"{accept} Pick up"
        };
    }

    private FrameworkElement CreateTileElement(ProfileTile tile, bool isActive)
    {
        var border = new Border
        {
            Width = tile.Width * CellSize - CellGap,
            Height = tile.Height * CellSize - CellGap,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tile.BackgroundPrimary)),
            BorderBrush = isActive ? Brushes.White : (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(isActive ? 2 : 1),
            CornerRadius = new CornerRadius(8)
        };
        var label = new TextBlock
        {
            Text = tile.Kind switch
            {
                ProfileTileKind.Text => tile.Title ?? "Text tile",
                ProfileTileKind.Link => tile.LinkUrl ?? "Link tile",
                ProfileTileKind.Stat => tile.StatValue ?? "Stat tile",
                ProfileTileKind.Media => "Picture tile",
                _ => tile.Kind.ToString()
            },
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tile.TextColour)),
            Margin = new Thickness(10),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        border.Child = label;
        Canvas.SetLeft(border, tile.X * CellSize + CellGap / 2.0);
        Canvas.SetTop(border, tile.Y * CellSize + CellGap / 2.0);
        return border;
    }

    private FrameworkElement CreateCursorElement()
    {
        var editor = _editor!;
        var (x, y, width, height) = editor.Mode == ProfileTileEditorMode.Browsing
            ? (editor.CursorX, editor.CursorY, 1, 1)
            : (editor.ActiveTile!.X, editor.ActiveTile.Y, editor.ActiveTile.Width, editor.ActiveTile.Height);

        var cursor = new Border
        {
            Width = width * CellSize - CellGap,
            Height = height * CellSize - CellGap,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x66)),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(6),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(cursor, x * CellSize + CellGap / 2.0);
        Canvas.SetTop(cursor, y * CellSize + CellGap / 2.0);
        Panel.SetZIndex(cursor, 10);
        return cursor;
    }
}
