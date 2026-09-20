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

    public event EventHandler? BackRequested;
    public event Action<string>? ActivateRequested;
    public event Action<ThemeDefinition, bool>? SaveRequested;
    public event Action<string>? DeleteRequested;
    public event EventHandler? NewThemeRequested;

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
        foreach (var field in ColorFields)
        {
            var swatch = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0), BorderBrush = (Brush)FindResource("CardBorderBrush"), BorderThickness = new Thickness(1) };
            var hexText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas"), FontSize = 14 };
            var button = new Button
            {
                Tag = field.Key,
                MinHeight = 54,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(10, 6, 14, 6),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = field.Label, FontSize = 12, Foreground = (Brush)FindResource("MutedBrush") },
                        new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Children = { swatch, hexText } }
                    }
                }
            };
            button.Click += ColorField_Click;
            ShellTileMotion.Attach(button);
            _fieldControls[field.Key] = (swatch, hexText);
            ColorFieldsPanel.Children.Add(button);
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

    public void SetSaveAvailability(bool canOverwrite, bool canDelete)
    {
        SaveThemeButton.IsEnabled = canOverwrite;
        SaveThemeButton.ToolTip = canOverwrite ? null : "Built-in themes can't be overwritten. Use Save as New Theme instead.";
        DeleteThemeButton.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
        _deleteArmed = false;
        DeleteThemeButton.Content = "Delete This Theme";
    }

    public void ShowStatus(string message) => StatusText.Text = message;

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
