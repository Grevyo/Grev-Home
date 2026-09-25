using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GrevHome.Input;
using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class ProfileTileEditorView : UserControl
{
    private const int CellSize = 56;
    private const int CellGap = 4;
    private static readonly ProfileTileKind[] AddKinds =
        [ProfileTileKind.Text, ProfileTileKind.Link, ProfileTileKind.Media, ProfileTileKind.Stat];
    private static readonly string[] ColourPresets =
        ["#11161d", "#3157c9", "#f4f7fb", "#394657", "#d4a72c", "#7c3aed", "#dc2626", "#059669", "#000000", "#ffffff"];

    private ProfileTileGridEditor? _editor;
    private bool _choosingAddKind;
    private int _addKindIndex;
    private bool _editingSettings;
    private int _settingsIndex;
    private TileSettingsField? _pendingKeyboardField;
    private string? _mediaRoot;
    private bool _pendingDiscardConfirm;

    public event EventHandler? BackRequested;
    public event Action<IReadOnlyList<ProfileTile>>? SaveRequested;
    public event EventHandler? ChooseMediaRequested;

    public bool IsControllerActive { get; set; } = true;

    /// <summary>True when the layout differs from what Load() last started with.</summary>
    public bool IsDirty => _editor?.IsDirty ?? false;

    /// <summary>Call after the host successfully persists the current tiles, so a later Back does
    /// not warn about changes that were, in fact, just saved.</summary>
    public void MarkSaved() => _editor?.MarkSaved();

    public ProfileTileEditorView()
    {
        InitializeComponent();
        KeyboardOverlay.Completed += OnKeyboardCompleted;
        Loaded += (_, _) => Focus();
    }

    public void Load(IReadOnlyList<ProfileTile> tiles, string? mediaRoot = null)
    {
        _choosingAddKind = false;
        _addKindIndex = 0;
        _editingSettings = false;
        _settingsIndex = 0;
        _pendingKeyboardField = null;
        _pendingDiscardConfirm = false;
        _mediaRoot = mediaRoot;
        _editor = new ProfileTileGridEditor(tiles);
        Render();
    }

    public bool HandleInput(InputAction action)
    {
        if (_editor is null) return false;
        if (action != InputAction.Back) _pendingDiscardConfirm = false;
        if (_editingSettings) return HandleSettingsInput(action);

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

        // Back reaches here only while just Browsing (Holding/Resizing/the add-kind picker all
        // consume their own Back above) - this is "leave the whole tile editor". Require a second
        // Back press when there are unsaved changes rather than silently discarding them; any other
        // input in between resets the confirmation (see the top of this method) so it can't be
        // triggered by an unrelated later Back press.
        if (action == InputAction.Back && _editor.IsDirty && !_pendingDiscardConfirm)
        {
            _pendingDiscardConfirm = true;
            PromptText.Text = "Unsaved changes - press Back again to discard them, or Save first.";
            return true;
        }

        return false;
    }

    public bool HandleExtendedControl(AppControllerControl control)
    {
        if (_editor is null) return false;
        if (_editingSettings)
        {
            if (control == AppControllerControl.Y)
            {
                CloseSettings();
                return true;
            }
            if (control == AppControllerControl.View)
            {
                RemoveActiveTile();
                return true;
            }
            return true;
        }

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
                RemoveActiveTile();
                return true;
            case AppControllerControl.RightShoulder:
                DuplicateActiveTile();
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
        var added = _editor.AddTile(kind, width, height);
        if (added is null)
        {
            PromptText.Text = _editor.Tiles.Count >= ProfileTileGrid.MaxTiles
                ? $"A profile can have up to {ProfileTileGrid.MaxTiles} tiles."
                : "There is no free space left for a new tile.";
            return;
        }

        var defaults = kind switch
        {
            ProfileTileKind.Text => added with { Title = "About me", Body = "Write something about yourself." },
            ProfileTileKind.Link => added with { Title = "My link", LinkLabel = "Open link" },
            ProfileTileKind.Media => added with { Title = "Picture tile", BackgroundType = ProfileTileBackgroundType.Media },
            ProfileTileKind.Stat => added with { Title = "Stat", StatValue = "100%" },
            _ => added
        };
        _editor.UpdateActiveTile(defaults);
    }

    private void EditTile_Click(object sender, RoutedEventArgs e) => BeginEditActiveTile();

    private void BeginEditActiveTile()
    {
        if (_editor?.ActiveTile is null)
        {
            PromptText.Text = "Pick up a tile first, then edit it.";
            return;
        }
        _editingSettings = true;
        _settingsIndex = 0;
        _pendingKeyboardField = null;
        Render();
    }

    private bool HandleSettingsInput(InputAction action)
    {
        var tile = _editor?.ActiveTile;
        if (tile is null)
        {
            CloseSettings();
            return true;
        }
        var fields = GetSettingsFields(tile);
        if (fields.Count == 0) return true;
        _settingsIndex = Math.Clamp(_settingsIndex, 0, fields.Count - 1);

        switch (action)
        {
            case InputAction.Up:
                _settingsIndex = (_settingsIndex + fields.Count - 1) % fields.Count;
                Render();
                return true;
            case InputAction.Down:
                _settingsIndex = (_settingsIndex + 1) % fields.Count;
                Render();
                return true;
            case InputAction.Left:
                AdjustSetting(fields[_settingsIndex], -1);
                Render();
                return true;
            case InputAction.Right:
                AdjustSetting(fields[_settingsIndex], 1);
                Render();
                return true;
            case InputAction.Accept:
                ActivateSetting(fields[_settingsIndex]);
                return true;
            case InputAction.Back:
                CloseSettings();
                return true;
            default:
                return true;
        }
    }

    private static IReadOnlyList<TileSettingsField> GetSettingsFields(ProfileTile tile)
    {
        var fields = new List<TileSettingsField> { TileSettingsField.Title, TileSettingsField.Body };
        if (tile.Kind == ProfileTileKind.Link)
        {
            fields.Add(TileSettingsField.LinkLabel);
            fields.Add(TileSettingsField.LinkUrl);
        }
        if (tile.Kind == ProfileTileKind.Stat) fields.Add(TileSettingsField.StatValue);
        fields.AddRange([
            TileSettingsField.BackgroundType,
            TileSettingsField.BackgroundPrimary,
            TileSettingsField.BackgroundSecondary,
            TileSettingsField.BackgroundAngle,
            TileSettingsField.Media,
            TileSettingsField.MediaFit,
            TileSettingsField.MediaOverlay,
            TileSettingsField.TextColour,
            TileSettingsField.BorderColour,
            TileSettingsField.FontFamily
        ]);
        return fields;
    }

    private void ActivateSetting(TileSettingsField field)
    {
        var tile = _editor?.ActiveTile;
        if (tile is null) return;
        switch (field)
        {
            case TileSettingsField.Title:
                OpenKeyboard(field, "Tile title", tile.Title, ProfileTileGrid.MaxTitleLength);
                break;
            case TileSettingsField.Body:
                OpenKeyboard(field, "Tile text", tile.Body, ProfileTileGrid.MaxBodyLength);
                break;
            case TileSettingsField.LinkLabel:
                OpenKeyboard(field, "Link label", tile.LinkLabel, ProfileTileGrid.MaxLinkLabelLength);
                break;
            case TileSettingsField.LinkUrl:
                OpenKeyboard(field, "Link URL (http:// or https://)", tile.LinkUrl, ProfileTileGrid.MaxLinkUrlLength);
                break;
            case TileSettingsField.StatValue:
                OpenKeyboard(field, "Stat value", tile.StatValue, ProfileTileGrid.MaxStatValueLength);
                break;
            case TileSettingsField.BackgroundPrimary:
            case TileSettingsField.BackgroundSecondary:
            case TileSettingsField.TextColour:
            case TileSettingsField.BorderColour:
                OpenKeyboard(field, $"{FieldLabel(field)} (#RRGGBB)", SettingValue(tile, field), 7);
                break;
            case TileSettingsField.Media:
                ChooseMediaRequested?.Invoke(this, EventArgs.Empty);
                break;
            default:
                AdjustSetting(field, 1);
                Render();
                break;
        }
    }

    private void OpenKeyboard(TileSettingsField field, string heading, string? value, int maxLength)
    {
        _pendingKeyboardField = field;
        KeyboardOverlay.Open(heading, value, maxLength);
    }

    private void AdjustSetting(TileSettingsField field, int direction)
    {
        var tile = _editor?.ActiveTile;
        if (tile is null) return;
        ProfileTile updated = tile;
        switch (field)
        {
            case TileSettingsField.BackgroundType:
                updated = tile with { BackgroundType = Cycle(tile.BackgroundType, direction) };
                break;
            case TileSettingsField.BackgroundPrimary:
                updated = tile with { BackgroundPrimary = CycleColour(tile.BackgroundPrimary, direction) };
                break;
            case TileSettingsField.BackgroundSecondary:
                updated = tile with { BackgroundSecondary = CycleColour(tile.BackgroundSecondary, direction) };
                break;
            case TileSettingsField.BackgroundAngle:
                updated = tile with { BackgroundAngle = Math.Clamp(tile.BackgroundAngle + direction * 5, 0, 360) };
                break;
            case TileSettingsField.MediaFit:
                updated = tile with { MediaFit = Cycle(tile.MediaFit, direction) };
                break;
            case TileSettingsField.MediaOverlay:
                updated = tile with { MediaOverlay = Cycle(tile.MediaOverlay, direction) };
                break;
            case TileSettingsField.TextColour:
                updated = tile with { TextColour = CycleColour(tile.TextColour, direction) };
                break;
            case TileSettingsField.BorderColour:
                updated = tile with { BorderColour = CycleColour(tile.BorderColour, direction) };
                break;
            case TileSettingsField.FontFamily:
                updated = tile with { FontFamily = Cycle(tile.FontFamily, direction) };
                break;
            default:
                return;
        }
        _editor!.UpdateActiveTile(updated);
    }

    private static T Cycle<T>(T value, int direction) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var index = Array.IndexOf(values, value);
        if (index < 0) index = 0;
        return values[(index + (direction >= 0 ? 1 : values.Length - 1)) % values.Length];
    }

    private static string CycleColour(string value, int direction)
    {
        var index = Array.FindIndex(ColourPresets, colour => string.Equals(colour, value, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = 0;
        return ColourPresets[(index + (direction >= 0 ? 1 : ColourPresets.Length - 1)) % ColourPresets.Length];
    }

    private void OnKeyboardCompleted(string value)
    {
        if (_editor?.ActiveTile is not { } tile || _pendingKeyboardField is not { } field) return;
        _pendingKeyboardField = null;
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        ProfileTile updated;
        switch (field)
        {
            case TileSettingsField.Title:
                updated = tile with { Title = text };
                break;
            case TileSettingsField.Body:
                updated = tile with { Body = text };
                break;
            case TileSettingsField.LinkLabel:
                updated = tile with { LinkLabel = text };
                break;
            case TileSettingsField.LinkUrl:
                updated = tile with { LinkUrl = text };
                break;
            case TileSettingsField.StatValue:
                updated = tile with { StatValue = text };
                break;
            case TileSettingsField.BackgroundPrimary:
            case TileSettingsField.BackgroundSecondary:
            case TileSettingsField.TextColour:
            case TileSettingsField.BorderColour:
                if (!ProfileTileGrid.IsValidHexColour(text))
                {
                    PromptText.Text = "Colours must use #RRGGBB, for example #3157c9.";
                    RenderSettingsPanel();
                    return;
                }
                updated = field switch
                {
                    TileSettingsField.BackgroundPrimary => tile with { BackgroundPrimary = text! },
                    TileSettingsField.BackgroundSecondary => tile with { BackgroundSecondary = text! },
                    TileSettingsField.TextColour => tile with { TextColour = text! },
                    _ => tile with { BorderColour = text! }
                };
                break;
            default:
                return;
        }
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

    private void RemoveTile_Click(object sender, RoutedEventArgs e) => RemoveActiveTile();

    private void RemoveActiveTile()
    {
        if (_editor?.RemoveActiveTile() != true)
        {
            PromptText.Text = "Pick up a tile first, then remove it.";
            return;
        }
        _editingSettings = false;
        _pendingKeyboardField = null;
        Render();
    }

    private void DuplicateTile_Click(object sender, RoutedEventArgs e) => DuplicateActiveTile();

    private void DuplicateActiveTile()
    {
        if (_editor is null) return;
        if (_editor.Mode != ProfileTileEditorMode.Holding)
        {
            PromptText.Text = "Pick up a tile first, then duplicate it.";
            return;
        }
        if (_editor.DuplicateActiveTile() is null)
        {
            PromptText.Text = _editor.Tiles.Count >= ProfileTileGrid.MaxTiles
                ? $"A profile can have up to {ProfileTileGrid.MaxTiles} tiles."
                : "There is no free space left for a copy of this tile.";
            return;
        }
        Render();
    }

    private void SettingsPrevious_Click(object sender, RoutedEventArgs e) => HandleSettingsInput(InputAction.Up);
    private void SettingsNext_Click(object sender, RoutedEventArgs e) => HandleSettingsInput(InputAction.Down);
    private void SettingsActivate_Click(object sender, RoutedEventArgs e) => HandleSettingsInput(InputAction.Accept);
    private void SettingsClose_Click(object sender, RoutedEventArgs e) => CloseSettings();

    private void CloseSettings()
    {
        _editingSettings = false;
        _pendingKeyboardField = null;
        Render();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(_editor?.Tiles ?? []);

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        // Same confirm-on-second-press rule as the controller/keyboard Back path in HandleInput,
        // so a stray mouse click can't discard unsaved work any more easily than a stray B press.
        if (_editor?.IsDirty == true && !_pendingDiscardConfirm)
        {
            _pendingDiscardConfirm = true;
            PromptText.Text = "Unsaved changes - select Back again to discard them, or Save first.";
            return;
        }
        _pendingDiscardConfirm = false;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Render()
    {
        GridCanvas.Children.Clear();
        if (_editor is null) return;

        GridCanvas.Width = ProfileTileGrid.Columns * CellSize;
        GridCanvas.Height = ProfileTileGrid.MaxRows * CellSize;
        foreach (var tile in _editor.Tiles)
            GridCanvas.Children.Add(CreateTileElement(tile, isActive: tile.TileId == _editor.ActiveTile?.TileId));
        GridCanvas.Children.Add(CreateCursorElement());

        SettingsPanel.Visibility = _editingSettings && _editor.ActiveTile is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_editingSettings) RenderSettingsPanel();

        if (_choosingAddKind)
        {
            var selected = AddKinds[_addKindIndex] == ProfileTileKind.Media ? "Picture / GIF" : AddKinds[_addKindIndex].ToString();
            HintText.Text = $"Choose tile type: {selected}. Use D-Pad/arrow keys to change type.";
            PromptText.Text = IsControllerActive ? "A Add    B Cancel" : "Enter Add    Esc Cancel";
        }
        else if (_editingSettings)
        {
            HintText.Text = "Edit every content and appearance field using the same profile-tile contract as Grev.dad.";
            PromptText.Text = IsControllerActive ? "D-Pad Change setting    A Edit/choose    B Close    Y Close    View Remove" : "Arrow keys Change setting    Enter Edit/choose    Esc Close";
        }
        else
        {
            HintText.Text = _editor.Mode switch
            {
                ProfileTileEditorMode.Holding => "Move the tile. X resizes, Y edits every tile setting, RB duplicates it, View removes it, A drops it and B cancels.",
                ProfileTileEditorMode.Resizing => "Resize with the D-Pad. A confirms and B reverts.",
                _ => "Move the cursor. A picks up a tile; A on an empty cell adds a new one."
            };
            PromptText.Text = BuildPrompt();
        }

        var targetTop = (_editor.Mode == ProfileTileEditorMode.Browsing ? _editor.CursorY : (_editor.ActiveTile?.Y ?? 0)) * CellSize;
        GridScroller.ScrollToVerticalOffset(Math.Max(0, targetTop - 200));
    }

    private void RenderSettingsPanel()
    {
        var tile = _editor?.ActiveTile;
        if (tile is null) return;
        var fields = GetSettingsFields(tile);
        if (fields.Count == 0) return;
        _settingsIndex = Math.Clamp(_settingsIndex, 0, fields.Count - 1);
        var field = fields[_settingsIndex];
        SettingsHeadingText.Text = tile.Title ?? (tile.Kind == ProfileTileKind.Media ? "Picture / GIF tile" : $"{tile.Kind} tile");
        SettingsPositionText.Text = $"Setting {_settingsIndex + 1} of {fields.Count}  •  {tile.Width}×{tile.Height} at {tile.X + 1},{tile.Y + 1}";
        SettingsFieldText.Text = FieldLabel(field).ToUpperInvariant();
        SettingsValueText.Text = SettingValue(tile, field);
        SettingsDetailText.Text = FieldDetail(field);
    }

    private string BuildPrompt()
    {
        var accept = IsControllerActive ? "A" : "Enter";
        var back = IsControllerActive ? "B" : "Esc";
        return _editor?.Mode switch
        {
            ProfileTileEditorMode.Holding when IsControllerActive => $"{accept} Drop    {back} Cancel    X Resize    Y Edit    RB Duplicate    View Remove",
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
            Background = CreateTileBackground(tile),
            BorderBrush = isActive ? Brushes.White : new SolidColorBrush(ParseColour(tile.BorderColour)),
            BorderThickness = new Thickness(isActive ? 2 : 1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true
        };

        var foreground = new SolidColorBrush(ParseColour(tile.TextColour));
        var content = new Grid();
        if (tile.BackgroundType == ProfileTileBackgroundType.Media && tile.MediaOverlay != ProfileTileMediaOverlay.None)
        {
            var overlayColour = tile.MediaOverlay == ProfileTileMediaOverlay.Dark ? Colors.Black : Colors.White;
            content.Children.Add(new Border
            {
                Background = new SolidColorBrush(overlayColour) { Opacity = tile.MediaOverlay == ProfileTileMediaOverlay.Dark ? 0.38 : 0.26 }
            });
        }

        var stack = new StackPanel { Margin = new Thickness(9), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = tile.Title ?? (tile.Kind == ProfileTileKind.Media ? "Picture tile" : $"{tile.Kind} tile"),
            Foreground = foreground,
            FontFamily = TileFont(tile.FontFamily),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (tile.Kind == ProfileTileKind.Stat && !string.IsNullOrWhiteSpace(tile.StatValue))
            stack.Children.Add(new TextBlock { Text = tile.StatValue, Foreground = foreground, FontFamily = TileFont(tile.FontFamily), FontSize = 20, FontWeight = FontWeights.Bold });
        if (!string.IsNullOrWhiteSpace(tile.Body))
            stack.Children.Add(new TextBlock { Text = tile.Body, Foreground = foreground, FontFamily = TileFont(tile.FontFamily), FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 48 });
        if (tile.Kind == ProfileTileKind.Link)
            stack.Children.Add(new TextBlock { Text = tile.LinkLabel ?? tile.LinkUrl ?? "Open link", Foreground = foreground, FontFamily = TileFont(tile.FontFamily), FontSize = 11, TextWrapping = TextWrapping.Wrap });
        if (tile.Kind == ProfileTileKind.Media && string.IsNullOrWhiteSpace(tile.BackgroundMediaFile))
            stack.Children.Add(new TextBlock { Text = "Choose a picture / GIF", Foreground = foreground, FontSize = 11 });
        content.Children.Add(stack);
        border.Child = content;
        Canvas.SetLeft(border, tile.X * CellSize + CellGap / 2.0);
        Canvas.SetTop(border, tile.Y * CellSize + CellGap / 2.0);
        return border;
    }

    private Brush CreateTileBackground(ProfileTile tile)
    {
        if (tile.BackgroundType == ProfileTileBackgroundType.Gradient)
            return new LinearGradientBrush(ParseColour(tile.BackgroundPrimary), ParseColour(tile.BackgroundSecondary), tile.BackgroundAngle);

        if (tile.BackgroundType == ProfileTileBackgroundType.Media &&
            !string.IsNullOrWhiteSpace(tile.BackgroundMediaFile) && !string.IsNullOrWhiteSpace(_mediaRoot))
        {
            var path = Path.Combine(_mediaRoot, Path.GetFileName(tile.BackgroundMediaFile));
            if (File.Exists(path))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return new ImageBrush(bitmap)
                    {
                        Stretch = tile.MediaFit switch
                        {
                            ProfileTileMediaFit.Contain => Stretch.Uniform,
                            ProfileTileMediaFit.Stretch => Stretch.Fill,
                            _ => Stretch.UniformToFill
                        },
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center
                    };
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
                {
                    // Fall through to the solid colour; saving still validates the file separately.
                }
            }
        }
        return new SolidColorBrush(ParseColour(tile.BackgroundPrimary));
    }

    private static Color ParseColour(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch (FormatException) { return Colors.White; }
    }

    private static FontFamily TileFont(ProfileTileFontFamily family) => new(family switch
    {
        ProfileTileFontFamily.Display => "Impact",
        ProfileTileFontFamily.Mono => "Consolas",
        ProfileTileFontFamily.Serif => "Georgia",
        ProfileTileFontFamily.Rounded => "Trebuchet MS",
        _ => "Segoe UI"
    });

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

    private static string FieldLabel(TileSettingsField field) => field switch
    {
        TileSettingsField.Title => "Title",
        TileSettingsField.Body => "Text",
        TileSettingsField.LinkLabel => "Link label",
        TileSettingsField.LinkUrl => "Link URL",
        TileSettingsField.StatValue => "Large value",
        TileSettingsField.BackgroundType => "Background",
        TileSettingsField.BackgroundPrimary => "First colour",
        TileSettingsField.BackgroundSecondary => "Second colour",
        TileSettingsField.BackgroundAngle => "Gradient angle",
        TileSettingsField.Media => "Picture / animated GIF",
        TileSettingsField.MediaFit => "Picture fit",
        TileSettingsField.MediaOverlay => "Overlay",
        TileSettingsField.TextColour => "Text colour",
        TileSettingsField.BorderColour => "Border colour",
        TileSettingsField.FontFamily => "Font",
        _ => field.ToString()
    };

    private static string FieldDetail(TileSettingsField field) => field switch
    {
        TileSettingsField.Title or TileSettingsField.Body or TileSettingsField.LinkLabel or TileSettingsField.LinkUrl or TileSettingsField.StatValue => "Press A to edit with the Grev Home controller keyboard.",
        TileSettingsField.BackgroundPrimary or TileSettingsField.BackgroundSecondary or TileSettingsField.TextColour or TileSettingsField.BorderColour => "Left/Right cycles useful presets. Press A to enter any #RRGGBB colour.",
        TileSettingsField.Media => "Press A to choose a PNG, JPG, BMP or animated GIF from Grev Home Files.",
        TileSettingsField.BackgroundAngle => "Left/Right adjusts by 5 degrees.",
        _ => "Use Left/Right to change this setting."
    };

    private static string SettingValue(ProfileTile tile, TileSettingsField field) => field switch
    {
        TileSettingsField.Title => tile.Title ?? "(none)",
        TileSettingsField.Body => tile.Body ?? "(none)",
        TileSettingsField.LinkLabel => tile.LinkLabel ?? "(none)",
        TileSettingsField.LinkUrl => tile.LinkUrl ?? "(none)",
        TileSettingsField.StatValue => tile.StatValue ?? "(none)",
        TileSettingsField.BackgroundType => tile.BackgroundType.ToString(),
        TileSettingsField.BackgroundPrimary => tile.BackgroundPrimary,
        TileSettingsField.BackgroundSecondary => tile.BackgroundSecondary,
        TileSettingsField.BackgroundAngle => $"{tile.BackgroundAngle}°",
        TileSettingsField.Media => tile.BackgroundMediaFile ?? "No picture selected",
        TileSettingsField.MediaFit => tile.MediaFit.ToString(),
        TileSettingsField.MediaOverlay => tile.MediaOverlay.ToString(),
        TileSettingsField.TextColour => tile.TextColour,
        TileSettingsField.BorderColour => tile.BorderColour,
        TileSettingsField.FontFamily => tile.FontFamily.ToString(),
        _ => string.Empty
    };

    private enum TileSettingsField
    {
        Title,
        Body,
        LinkLabel,
        LinkUrl,
        StatValue,
        BackgroundType,
        BackgroundPrimary,
        BackgroundSecondary,
        BackgroundAngle,
        Media,
        MediaFit,
        MediaOverlay,
        TextColour,
        BorderColour,
        FontFamily
    }
}
