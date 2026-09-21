using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Presentation;

namespace GrevHome.Views;

public partial class ThemeCreatorView : UserControl
{
    /// <summary>
    /// The editable color fields exposed by the creator. Card border and the three role colors
    /// are intentionally not exposed here: they carry over unchanged from whichever theme editing
    /// started from, keeping the editor to the handful of colors that actually define a theme's
    /// character while still producing a fully valid ThemeDefinition.
    /// </summary>
    private static readonly (string Key, string Label, Func<ThemeDefinition, string> Get, Func<ThemeDefinition, string, ThemeDefinition> With)[] ColorFields =
    [
        ("accent", "Accent", theme => theme.Accent, (theme, value) => theme with { Accent = value }),
        ("surface", "Surface", theme => theme.Surface, (theme, value) => theme with { Surface = value }),
        ("surfaceHover", "Surface Hover", theme => theme.SurfaceHover, (theme, value) => theme with { SurfaceHover = value }),
        ("windowBackground", "Window Background", theme => theme.WindowBackground, (theme, value) => theme with { WindowBackground = value }),
        ("cardBackground", "Card Background", theme => theme.CardBackground, (theme, value) => theme with { CardBackground = value }),
        ("muted", "Muted Text", theme => theme.Muted, (theme, value) => theme with { Muted = value })
    ];

    /// <summary>
    /// A generic quick-pick palette shown under every field, the same "click a swatch instead of
    /// typing a hex code" convention already used by Dashboard tile artwork, game tile colors and
    /// profile presets elsewhere in the app. Exact colors remain reachable through Enter Hex.
    /// </summary>
    private static readonly (string Name, string Hex)[] PresetSwatches =
    [
        ("Blue", "#7EA6FF"), ("Violet", "#B18CFF"), ("Pink", "#E85D75"), ("Orange", "#FF9A5A"),
        ("Yellow", "#F2C94C"), ("Green", "#6FD6A0"), ("Teal", "#4DD0E1"),
        ("Charcoal", "#151923"), ("Near Black", "#0B0A14"), ("White", "#F5F5F5")
    ];

    public event EventHandler? BackRequested;
    public event Action<string>? ActivateRequested;
    public event Action<ThemeDefinition, bool>? SaveRequested;
    public event Action<string>? DeleteRequested;
    public event EventHandler? NewThemeRequested;
    public event Action<ThemeDefinition>? ExportRequested;
    public event EventHandler? ImportRequested;
    public event EventHandler? SwitchScopeRequested;
    public event EventHandler? UseMachineDefaultRequested;

    private ThemeDefinition _editing = ThemeCatalog.Default;
    private string? _pendingField;
    private bool _deleteArmed;
    private readonly Dictionary<string, (Border Swatch, TextBlock HexText)> _fieldControls = new();

    public ThemeCreatorView()
    {
        InitializeComponent();
        KeyboardOverlay.Completed += OnKeyboardCompleted;
        KeyboardOverlay.Cancelled += (_, _) => _pendingField = null;
        BuildColorFields();
    }

    private void BuildColorFields()
    {
        ColorFieldsPanel.Children.Clear();
        _fieldControls.Clear();
        var cardBorderBrush = (Brush)FindResource("CardBorderBrush");
        var mutedBrush = (Brush)FindResource("MutedBrush");
        foreach (var field in ColorFields)
        {
            var swatch = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0), BorderBrush = cardBorderBrush, BorderThickness = new Thickness(1) };
            var hexText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas"), FontSize = 14 };

            var enterHexButton = new Button { Tag = field.Key, Content = "Enter Hex", MinHeight = 38, MinWidth = 96, Margin = new Thickness(10, 0, 0, 0), FontSize = 12 };
            enterHexButton.Click += ColorField_Click;
            ShellTileMotion.Attach(enterHexButton);

            var presetsPanel = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var preset in PresetSwatches)
            {
                // The swatch color lives on an inner Border, the same convention
                // AppArtworkFactory uses for every tile in the app: the shared Button style's
                // IsMouseOver trigger sets the button's own Background, and that trigger is
                // inherited by every style based on it (SharpTileButtonStyle included), so a
                // color set directly on the button itself would go gray the moment it's hovered
                // - exactly the moment a color swatch most needs to still show its color.
                var presetButton = new Button
                {
                    Style = (Style)FindResource("SharpTileButtonStyle"),
                    Tag = (field.Key, preset.Hex),
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(0),
                    ToolTip = $"{preset.Name} ({preset.Hex})",
                    Content = new Border
                    {
                        // Explicit size rather than relying on Stretch: the base Button style
                        // centers content instead of stretching it, so an unsized Border here
                        // would collapse to nothing and the swatch would be invisible.
                        Width = 22,
                        Height = 22,
                        CornerRadius = new CornerRadius(3),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.Hex)!),
                        BorderBrush = cardBorderBrush,
                        BorderThickness = new Thickness(1)
                    }
                };
                presetButton.Click += ColorPreset_Click;
                ShellTileMotion.Attach(presetButton);
                presetsPanel.Children.Add(presetButton);
            }

            var block = new Border
            {
                Width = 230,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(14),
                BorderBrush = cardBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = field.Label, FontSize = 12, Foreground = mutedBrush },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(0, 6, 0, 0),
                            Children = { swatch, hexText, enterHexButton }
                        },
                        presetsPanel
                    }
                }
            };

            _fieldControls[field.Key] = (swatch, hexText);
            ColorFieldsPanel.Children.Add(block);
        }
    }

    /// <summary>
    /// Updates the editing draft and previews it live across the whole shell immediately - every
    /// element using the theme brushes is DynamicResource-bound, so an in-progress edit is visible
    /// everywhere, not just in this page's own preview panel. Nothing here persists anything;
    /// Save/Save as New/Delete are the only calls that touch disk.
    /// </summary>
    public void SetEditing(ThemeDefinition theme)
    {
        _editing = theme;
        ThemeNameBox.Text = theme.Name;
        foreach (var field in ColorFields)
        {
            if (!_fieldControls.TryGetValue(field.Key, out var controls)) continue;
            var hex = field.Get(theme);
            controls.Swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            controls.HexText.Text = hex.ToUpperInvariant();
        }
        ThemeApplier.Apply(theme);

        var warnings = theme.GetContrastWarnings();
        ContrastWarningText.Text = warnings.Count == 0 ? string.Empty : string.Join(" ", warnings);
        ContrastWarningText.Visibility = warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetGallery(IReadOnlyList<ThemeDefinition> themes, string activeThemeId, string editingThemeId)
    {
        ThemeGalleryPanel.Children.Clear();
        foreach (var theme in themes)
        {
            var isActive = string.Equals(theme.Id, activeThemeId, StringComparison.OrdinalIgnoreCase);
            var isEditing = string.Equals(theme.Id, editingThemeId, StringComparison.OrdinalIgnoreCase);
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var hex in new[] { theme.Accent, theme.Surface, theme.WindowBackground })
            {
                swatches.Children.Add(new Border
                {
                    Width = 18,
                    Height = 18,
                    Margin = new Thickness(0, 0, 4, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!)
                });
            }
            var button = new Button
            {
                Tag = theme.Id,
                Width = 170,
                Height = 96,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(14, 12, 14, 12),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(isEditing ? 2 : 1),
                BorderBrush = isEditing ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("CardBorderBrush"),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = theme.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                        new TextBlock
                        {
                            Text = isActive ? "Active" : theme.IsBuiltIn ? "Built-in" : "Custom",
                            FontSize = 11,
                            Margin = new Thickness(0, 2, 0, 0),
                            Foreground = isActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("MutedBrush")
                        },
                        swatches
                    }
                }
            };
            button.Click += ThemeTile_Click;
            ShellTileMotion.Attach(button);
            ThemeGalleryPanel.Children.Add(button);
        }
    }

    public void SetSaveAvailability(bool canEdit, bool canOverwrite, bool canDelete)
    {
        SaveThemeButton.IsEnabled = canOverwrite;
        SaveThemeButton.ToolTip = canOverwrite ? null : "Built-in themes can't be overwritten. Use Save as New Theme instead.";
        SaveAsNewThemeButton.IsEnabled = canEdit;
        NewThemeButton.IsEnabled = canEdit;
        ImportThemeButton.IsEnabled = canEdit;
        DeleteThemeButton.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
        _deleteArmed = false;
        DeleteThemeButton.Content = "Delete This Theme";
    }

    public void ShowStatus(string message) => StatusText.Text = message;

    public void SetScope(bool machineScope, bool canManageMachine, bool inheritsMachine)
    {
        ThemeScopeTitle.Text = machineScope ? "Machine Default Theme" : "My Profile Theme";
        ThemeScopeDescription.Text = machineScope
            ? "This is the default for Guest and every GrevID that has not selected its own theme. Only an Admin can change it."
            : inheritsMachine
                ? "This GrevID currently follows the Admin's machine default. Choose a theme below to create a private profile override."
                : "This GrevID has its own private theme override. Other profiles and the machine default are unaffected.";
        SwitchScopeButton.Visibility = canManageMachine ? Visibility.Visible : Visibility.Collapsed;
        SwitchScopeButton.Content = machineScope ? "Manage My Profile Theme" : "Manage Machine Default";
        UseMachineDefaultButton.Visibility = machineScope ? Visibility.Collapsed : Visibility.Visible;
        UseMachineDefaultButton.IsEnabled = !inheritsMachine;
    }

    private void ThemeTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string themeId }) ActivateRequested?.Invoke(themeId);
    }

    private void ColorField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        var field = ColorFields.First(candidate => candidate.Key == key);
        _pendingField = key;
        KeyboardOverlay.Open($"Enter {field.Label} hex (without #)", field.Get(_editing).TrimStart('#'), 6);
    }

    private void ColorPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: (string key, string hex) }) return;
        var field = ColorFields.First(candidate => candidate.Key == key);
        SetEditing(field.With(_editing, hex));
    }

    private void RenameTheme_Click(object sender, RoutedEventArgs e)
    {
        _pendingField = "name";
        KeyboardOverlay.Open("Enter Theme Name", _editing.Name, 60);
    }

    private void OnKeyboardCompleted(string value)
    {
        var field = _pendingField;
        _pendingField = null;
        if (field is null) return;

        if (field == "name")
        {
            if (!string.IsNullOrWhiteSpace(value)) SetEditing(_editing with { Name = value.Trim() });
            return;
        }

        var normalized = value.Trim().TrimStart('#').ToUpperInvariant();
        if (normalized.Length != 6 || !normalized.All(Uri.IsHexDigit))
        {
            ShowStatus("Enter exactly 6 hex digits, like 7EA6FF.");
            return;
        }

        var colorField = ColorFields.First(candidate => candidate.Key == field);
        SetEditing(colorField.With(_editing, "#" + normalized));
    }

    private void NewTheme_Click(object sender, RoutedEventArgs e) => NewThemeRequested?.Invoke(this, EventArgs.Empty);
    private void SaveTheme_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(_editing, false);
    private void SaveAsNewTheme_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(_editing, true);
    private void ExportTheme_Click(object sender, RoutedEventArgs e) => ExportRequested?.Invoke(_editing);
    private void ImportTheme_Click(object sender, RoutedEventArgs e) => ImportRequested?.Invoke(this, EventArgs.Empty);
    private void SwitchScope_Click(object sender, RoutedEventArgs e) => SwitchScopeRequested?.Invoke(this, EventArgs.Empty);
    private void UseMachineDefault_Click(object sender, RoutedEventArgs e) => UseMachineDefaultRequested?.Invoke(this, EventArgs.Empty);

    // Deliberate two-step confirm, the same shape App Killer's Force Close uses: a single
    // accidental press on a destructive action must never delete a saved theme outright.
    private void DeleteTheme_Click(object sender, RoutedEventArgs e)
    {
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            DeleteThemeButton.Content = "Press Again to Delete";
            return;
        }

        _deleteArmed = false;
        DeleteThemeButton.Content = "Delete This Theme";
        DeleteRequested?.Invoke(_editing.Id);
    }
    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
