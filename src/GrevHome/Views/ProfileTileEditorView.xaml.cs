using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GrevHome.Input;
using GrevHome.Profiles;

namespace GrevHome.Views;

/// <summary>
/// Controller-first profile tile editor. Renders ProfileTileGridEditor's cursor state onto a
/// scrollable grid. The shell forwards its existing InputAction stream here; this view does not
/// create a second controller poller, so one physical input can never move both the grid and page
/// focus at the same time.
/// </summary>
public partial class ProfileTileEditorView : UserControl
{
    private const int CellSize = 56;
    private const int CellGap = 4;
    private static readonly ProfileTileKind[] AddKinds =
        [ProfileTileKind.Text, ProfileTileKind.Link, ProfileTileKind.Media, ProfileTileKind.Stat];

    private ProfileTileGridEditor? _editor;
    private bool _choosingAddKind;
    private int _addKindIndex;

    public event EventHandler? BackRequested;
    public event Action<IReadOnlyList<ProfileTile>>? SaveRequested;
    public event EventHandler? ChooseMediaRequested;

    /// <summary>Set by the host whenever controller/keyboard input arrives so prompt wording tracks
    /// the user's most recent input device.</summary>
    public bool IsControllerActive { get; set; } = true;

    public ProfileTileEditorView()
    {
        InitializeComponent();
        KeyboardOverlay.Completed += OnKeyboardCompleted;
        Loaded += (_, _) => Focus();
    }

    public void Load(IReadOnlyList<ProfileTile> tiles)
    {
        _choosingAddKind = false;
        _addKindIndex = 0;
        _editor = new ProfileTileGridEditor(tiles);
        Render();
    }

    /// <summary>Feeds one shell navigation action into the tile editor. Accept on an empty grid
    /// cell opens an entirely controller-driven tile-type chooser: D-Pad changes type, A confirms,
    /// B cancels.</summary>
    public bool HandleInput(InputAction action)
    {
        if (_editor is null) return false;

        if (_choosingAddKind)
        {
            switch (action)
            {
                case InputAction.Left or InputAction.Up:
                    _addKindIndex = (_addKindIndex + AddKinds.Length - 1) % AddKinds.Length;
                    Render();
                    return true;
                case InputAction.Right or InputAction.Down:
                    _addKindIndex = (_addKindIndex + 1) % AddKinds.Length;
                    Render();
                    return true;
                case InputAction.Accept:
                    AddTile(AddKinds[_addKindIndex]);
                    _choosingAddKind = false;
                    Render();
                    return true;
                case InputAction.Back:
                    _choosingAddKind = false;
                    Render();
                    return true;
            }
        }

        var consumed = _editor.HandleInput(action);
        if (consumed)
        {
            Render();
            return true;
        }

        if (action == InputAction.Accept && _editor.Mode == ProfileTileEditorMode.Browsing &&
            _editor.TileAt(_editor.CursorX, _editor.CursorY) is null)
        {
            _choosingAddKind = true;
            _addKindIndex = 0;
            Render();
            return true;
        }

        return false;
    }

    /// <summary>Extended buttons are supplied by ControllerInputService while this route has app
    /// input mode: X=resizing, Y=edit content/media, View=remove selected tile.</summary>
    public bool HandleExtendedControl(AppControllerControl control)
    {
        if (_editor is null) return false;
        switch (control)
        {
            case AppControllerControl.X:
                if (!_editor.BeginResize()) return false;
                Render();
                return true;
            case AppControllerControl.Y:
                BeginEditActiveTile();
                return true;
            case AppControllerControl.View:
                if (!_editor.RemoveActiveTile()) return false;
                Render();
                return true;
            default:
                return false;
        }
    }

    public void ShowStatus(string message) => PromptText.Text = message;

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
        if (sender is not Button { Tag: string tagKind } ||
            !Enum.TryParse<ProfileTileKind>(tagKind, out var kind)) return;
        AddTile(kind);
        Render();
    }

    private void AddTile(ProfileTileKind kind)
    {
        if (_editor is null) return;
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
            PromptText.Text = _editor.Tiles.Count >= ProfileTileGrid.MaxTiles
                ? $"A profile can have up to {ProfileTileGrid.MaxTiles} tiles."
                : "There is no free space left for a new tile.";
        }
    }

    private void EditTile_Click(object sender, RoutedEventArgs e) => BeginEditActiveTile();

    private void BeginEditActiveTile()
    {
        if (_editor?.ActiveTile is not { } tile)
        {
            PromptText.Text = "Pick up a tile first, then edit it.";
            return;
        }
        switch (tile.Kind)
        {
            case ProfileTileKind.Text:
                KeyboardOverlay.Open("Tile title", tile.Title, ProfileTileGrid.MaxTitleLength);
                break;
            case ProfileTileKind.Link:
                KeyboardOverlay.Open("Link URL (http:// or https://)", tile.LinkUrl, ProfileTileGrid.MaxLinkUrlLength);
                break;
            case ProfileTileKind.Stat:
                KeyboardOverlay.Open("Stat value", tile.StatValue, ProfileTileGrid.MaxStatValueLength);
                break;
            case ProfileTileKind.Media:
                ChooseMediaRequested?.Invoke(this, EventArgs.Empty);
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
        if (!_editor.UpdateActiveTile(updated))
        {
            PromptText.Text = "That tile could not be updated.";
            return;
        }
        Render();
    }

    public bool SetActiveMedia(string mediaFileName)
    {
        if (_editor?.ActiveTile is not { } tile) return false;
        var updated = tile with
        {
            BackgroundMediaFile = mediaFileName,
            BackgroundType = ProfileTileBackgroundType.Media
        };
        if (!_editor.UpdateActiveTile(updated)) return false;
        Render();
        return true;
    }

    private void RemoveTile_Click(object sender, RoutedEventArgs e)
    {
        if (_editor?.RemoveActiveTile() != true)
        {
            PromptText.Text = "Pick up a tile first, then remove it.";
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

        if (_choosingAddKind)
        {
            var selected = AddKinds[_addKindIndex] == ProfileTileKind.Media ? "Picture / GIF" : AddKinds[_addKindIndex].ToString();
            HintText.Text = $"Choose tile type: {selected}. Use D-Pad/arrow keys to change type.";
            PromptText.Text = IsControllerActive ? "A Add    B Cancel" : "Enter Add    Esc Cancel";
        }
        else
        {
            HintText.Text = _editor.Mode switch
            {
                ProfileTileEditorMode.Holding =>
                    "Move the tile. X resizes, Y edits it, View removes it, A drops it and B cancels.",
                ProfileTileEditorMode.Resizing => "Resize with the D-Pad. A confirms and B reverts.",
                _ => "Move the cursor. A picks up a tile; A on an empty cell adds a new one."
            };
            PromptText.Text = BuildPrompt();
        }

        var targetTop = (_editor.Mode == ProfileTileEditorMode.Browsing ? _editor.CursorY : (_editor.ActiveTile?.Y ?? 0)) * CellSize;
        GridScroller.ScrollToVerticalOffset(Math.Max(0, targetTop - 200));
    }

    private string BuildPrompt()
    {
        var accept = IsControllerActive ? "A" : "Enter";
        var back = IsControllerActive ? "B" : "Esc";
        return _editor?.Mode switch
        {
            ProfileTileEditorMode.Holding when IsControllerActive => $"{accept} Drop    {back} Cancel    X Resize    Y Edit    View Remove",
            ProfileTileEditorMode.Holding => $"{accept} Drop    {back} Cancel",
            ProfileTileEditorMode.Resizing => $"{accept} Confirm    {back} Revert",
            _ => $"{accept} Pick up / add"
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
                ProfileTileKind.Media => string.IsNullOrWhiteSpace(tile.BackgroundMediaFile) ? "Picture tile • choose media" : "Picture tile",
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
