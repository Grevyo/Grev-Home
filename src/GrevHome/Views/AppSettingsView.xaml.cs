using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Apps;
using GrevHome.Input;
using GrevHome.Sessions;
using GrevHome.Store;

namespace GrevHome.Views;

public sealed record AppControllerProfileDraft(
    bool Enabled,
    IReadOnlyList<AppControllerMapping> Mappings);

public partial class AppSettingsView : UserControl
{
    private readonly Dictionary<AppControllerControl, AppControllerOutput> _outputs = new();
    private bool _enabled;
    private bool _canSave;
    private bool _hasUserOverride;
    private bool _hasPresentationOverride;
    private bool _generalSectionExpanded;
    private bool _presentationSectionExpanded;
    private bool _controllerSectionExpanded;

    public event Action<AppControllerProfileDraft>? SaveRequested;
    public event EventHandler? ResetRequested;
    public event EventHandler? ResetOnboardingRequested;
    public event EventHandler? ResetPresentationRequested;
    public event EventHandler? BackRequested;

    public AppSettingsView()
    {
        InitializeComponent();
        UpdateSectionPresentation();
    }

    public void SetApp(
        InstalledAppEntry entry,
        GrevStorePackageDefinition? package,
        SessionUser? primaryUser,
        ResolvedAppControllerProfile profile,
        ResolvedAppPresentation presentation,
        bool canSave)
    {
        _enabled = profile.Enabled;
        _canSave = canSave;
        _hasUserOverride = profile.HasUserOverride;
        _hasPresentationOverride = presentation.HasUserOverrides;
        _outputs.Clear();
        foreach (var mapping in profile.Mappings)
        {
            _outputs[mapping.Control] = mapping.Output;
        }

        var definition = entry.Manifest.Definition;
        var displayName = presentation.DisplayName;
        AppNameText.Text = displayName;
        AppIdentityText.Text = primaryUser is null
            ? $"{displayName} • no Primary User"
            : string.IsNullOrWhiteSpace(primaryUser.GrevId)
                ? $"{displayName} • {primaryUser.DisplayName} • Guest settings are not persisted"
                : $"{displayName} • {primaryUser.DisplayName} • {primaryUser.GrevId}";

        NativeSupportText.Text = definition.SupportsController
            ? "This app declares native controller support. Its Grev controller profile can stay completely blank unless you want Grev Home to augment the app later."
            : "This app does not declare native controller support. Grev Home can use this profile as the standardized mapping contract for controller enhancement.";

        var onboarding = package?.Onboarding;
        OnboardingPanel.Visibility = onboarding is null ? Visibility.Collapsed : Visibility.Visible;
        if (onboarding is not null)
        {
            OnboardingTitleText.Text = onboarding.Title;
            OnboardingSummaryText.Text = onboarding.Summary;
            ResetOnboardingButton.IsEnabled = canSave;
            ResetOnboardingButton.Content = canSave
                ? "Show Launch Guide Again"
                : "Show Launch Guide Again (persistent GrevID required)";
        }

        var packageDefaults = package?.Presentation;
        PresentationSourceText.Text = presentation.HasUserOverrides
            ? $"Using {primaryUser?.DisplayName ?? "this user's"} per-GrevID presentation overrides."
            : packageDefaults is null
                ? "Using the installed app's built-in presentation because no Grev Store package defaults are registered."
                : "Using the presentation defaults supplied by this Grev Home package.";
        PresentationCurrentText.Text =
            $"Current: {presentation.DisplayName} • tile {presentation.TileColor} • " +
            $"icon {(string.IsNullOrWhiteSpace(presentation.IconPath) ? "default placeholder" : "configured")} • " +
            $"tile media {(string.IsNullOrWhiteSpace(presentation.TileMediaPath) ? "none" : "configured")} • " +
            $"hero media {(string.IsNullOrWhiteSpace(presentation.HeroMediaPath) ? "none" : "configured")}.";
        PresentationDefaultText.Text = packageDefaults is null
            ? "Package defaults: not registered for this app."
            : $"Package default: {packageDefaults.DisplayName} • tile {packageDefaults.TileColor} • " +
              $"icon {(string.IsNullOrWhiteSpace(packageDefaults.IconAsset) ? "neutral placeholder" : "supplied")}.";
        ResetPresentationButton.IsEnabled = canSave &&
                                            _hasPresentationOverride &&
                                            package?.Supports(AppPackageCapability.PresentationOverrides) == true;

        var appDefaultsPopulated = package?.ControllerProfile?.Mappings?.Any(mapping =>
            mapping.Output.Kind != AppControllerOutputKind.None) == true;
        ControllerSourceText.Text = profile.HasUserOverride
            ? $"Using {primaryUser?.DisplayName ?? "this user's"} custom controller profile."
            : appDefaultsPopulated
                ? "Using the controller profile supplied by this Grev Home package."
                : "No Grev controller mappings are supplied by this app. The standardized layout is available and currently blank.";

        SaveButton.IsEnabled = canSave;
        ResetButton.IsEnabled = canSave && profile.HasUserOverride;
        ControllerProfileToggleButton.IsEnabled = canSave;
        StatusText.Text = canSave
            ? "All app-specific overrides are scoped to this GrevID. Controller, onboarding and presentation defaults can each be restored independently."
            : "A persistent local Primary GrevID is required to save app-specific settings. Current package defaults are shown read-only.";

        UpdateSectionPresentation();
        UpdateTogglePresentation();
        RenderMappings();
    }

    public void ShowStatus(string message) => StatusText.Text = message;

    private void RenderMappings()
    {
        MappingsPanel.Children.Clear();

        foreach (var control in AppControllerProfileLayout.Controls)
        {
            var output = _outputs.TryGetValue(control, out var configured)
                ? configured
                : new AppControllerOutput(AppControllerOutputKind.None);

            var row = new Border
            {
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 6),
                MinHeight = 58,
                Background = new SolidColorBrush(Color.FromRgb(9, 12, 18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(43, 51, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(178) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            grid.Children.Add(new TextBlock
            {
                Text = AppControllerProfileLayout.FormatControl(control),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 10, 2)
            });

            var mappingText = new TextBlock
            {
                Text = AppControllerOutputCatalog.Format(output),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 2, 12, 2),
                Foreground = output.Kind == AppControllerOutputKind.None
                    ? (Brush)FindResource("MutedBrush")
                    : (Brush)FindResource("AccentBrush"),
                FontWeight = output.Kind == AppControllerOutputKind.None ? FontWeights.Normal : FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(mappingText, 1);
            grid.Children.Add(mappingText);

            var previous = new Button
            {
                Content = "◀",
                MinHeight = 42,
                Margin = new Thickness(4, 0, 4, 0),
                Tag = control,
                IsEnabled = _canSave,
                VerticalAlignment = VerticalAlignment.Center
            };
            previous.Click += PreviousMapping_Click;
            Grid.SetColumn(previous, 2);
            grid.Children.Add(previous);

            var next = new Button
            {
                Content = "▶",
                MinHeight = 42,
                Margin = new Thickness(4, 0, 4, 0),
                Tag = control,
                IsEnabled = _canSave,
                VerticalAlignment = VerticalAlignment.Center
            };
            next.Click += NextMapping_Click;
            Grid.SetColumn(next, 3);
            grid.Children.Add(next);

            var clear = new Button
            {
                Content = "Clear",
                MinHeight = 42,
                Margin = new Thickness(4, 0, 0, 0),
                Tag = control,
                IsEnabled = _canSave && output.Kind != AppControllerOutputKind.None,
                VerticalAlignment = VerticalAlignment.Center
            };
            clear.Click += ClearMapping_Click;
            Grid.SetColumn(clear, 4);
            grid.Children.Add(clear);

            row.Child = grid;
            MappingsPanel.Children.Add(row);
        }
    }

    private void GeneralSectionToggle_Click(object sender, RoutedEventArgs e)
    {
        _generalSectionExpanded = !_generalSectionExpanded;
        UpdateSectionPresentation();
    }

    private void PresentationSectionToggle_Click(object sender, RoutedEventArgs e)
    {
        _presentationSectionExpanded = !_presentationSectionExpanded;
        UpdateSectionPresentation();
    }

    private void ControllerSectionToggle_Click(object sender, RoutedEventArgs e)
    {
        _controllerSectionExpanded = !_controllerSectionExpanded;
        UpdateSectionPresentation();
    }

    private void UpdateSectionPresentation()
    {
        GeneralSectionPanel.Visibility = _generalSectionExpanded ? Visibility.Visible : Visibility.Collapsed;
        PresentationSectionPanel.Visibility = _presentationSectionExpanded ? Visibility.Visible : Visibility.Collapsed;
        ControllerSectionPanel.Visibility = _controllerSectionExpanded ? Visibility.Visible : Visibility.Collapsed;
        GeneralSectionToggleButton.Content = _generalSectionExpanded ? "GENERAL  ▴" : "GENERAL  ▾";
        PresentationSectionToggleButton.Content = _presentationSectionExpanded ? "PRESENTATION  ▴" : "PRESENTATION  ▾";
        ControllerSectionToggleButton.Content = _controllerSectionExpanded ? "CONTROLLER PROFILE  ▴" : "CONTROLLER PROFILE  ▾";
    }

    private void ControllerProfileToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_canSave) return;
        _enabled = !_enabled;
        UpdateTogglePresentation();
        StatusText.Text = _enabled
            ? "Controller profile enabled. Configured mappings will be available to this app's Grev controller integration."
            : "Controller profile disabled. Mappings are kept but will remain inactive until the profile is enabled again.";
    }

    private void PreviousMapping_Click(object sender, RoutedEventArgs e) => MoveMapping(sender, -1);
    private void NextMapping_Click(object sender, RoutedEventArgs e) => MoveMapping(sender, 1);

    private void MoveMapping(object sender, int delta)
    {
        if (!_canSave || sender is not Button { Tag: AppControllerControl control }) return;
        var current = _outputs.TryGetValue(control, out var output)
            ? output
            : new AppControllerOutput(AppControllerOutputKind.None);
        _outputs[control] = AppControllerOutputCatalog.Move(current, delta);
        RenderMappings();
        FocusMatchingButton(control, delta < 0 ? 2 : 3);
    }

    private void ClearMapping_Click(object sender, RoutedEventArgs e)
    {
        if (!_canSave || sender is not Button { Tag: AppControllerControl control }) return;
        _outputs[control] = new AppControllerOutput(AppControllerOutputKind.None);
        RenderMappings();
        FocusMatchingButton(control, 4);
    }

    private void FocusMatchingButton(AppControllerControl control, int column)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (var border in MappingsPanel.Children.OfType<Border>())
            {
                if (border.Child is not Grid grid) continue;
                var button = grid.Children.OfType<Button>()
                    .FirstOrDefault(candidate => candidate.Tag is AppControllerControl tagged && tagged == control && Grid.GetColumn(candidate) == column);
                if (button is not null)
                {
                    button.Focus();
                    return;
                }
            }
        }));
    }

    private void UpdateTogglePresentation()
    {
        ControllerProfileToggleButton.Content = _enabled
            ? "Controller Profile: Enabled"
            : "Controller Profile: Disabled";
        ControllerProfileToggleButton.BorderBrush = _enabled
            ? (Brush)FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromRgb(52, 61, 81));
    }

    private AppControllerProfileDraft CaptureDraft() => new(
        _enabled,
        AppControllerProfileLayout.Controls
            .Select(control => new AppControllerMapping(
                control,
                _outputs.TryGetValue(control, out var output)
                    ? output
                    : new AppControllerOutput(AppControllerOutputKind.None)))
            .ToArray());

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_canSave) SaveRequested?.Invoke(CaptureDraft());
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_canSave && _hasUserOverride) ResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ResetOnboarding_Click(object sender, RoutedEventArgs e)
    {
        if (_canSave) ResetOnboardingRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ResetPresentation_Click(object sender, RoutedEventArgs e)
    {
        if (_canSave && _hasPresentationOverride) ResetPresentationRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
