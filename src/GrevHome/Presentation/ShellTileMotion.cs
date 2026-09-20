using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace GrevHome.Presentation;

/// <summary>
/// One shared implementation of Grev Home tile motion: hover, controller/keyboard focus, press
/// and carousel reveal. Every tile surface (Dashboard, Settings hub, Store, Installed Apps) calls
/// this rather than animating buttons itself, so a mouse hover and a controller focus produce the
/// same physical response and motion settings only have to be honoured in one place.
///
/// The helper owns a tile's RenderTransform. Callers must not assign their own transform to an
/// attached button or the two owners will fight over the same property.
/// </summary>
public static class ShellTileMotion
{
    private const double RestScale = 1d;
    private const double ActiveScale = 1.045;
    private const double PressScale = .975;
    private const double ActiveLift = -5d;
    private const double GlowBlur = 26d;
    private const double GlowOpacity = .62;

    private static readonly Color GlowColor = Color.FromRgb(0x7E, 0xA6, 0xFF);
    private static ShellMotionSettings _settings = new();

    /// <summary>
    /// Applies the machine's current theme/motion settings. Disabling hover effects immediately
    /// returns every attached tile to rest the next time it is interacted with, and no animation
    /// is started while the setting is off.
    /// </summary>
    public static void Configure(ShellMotionSettings settings) => _settings = settings;

    private static double SpeedFactor => _settings.AnimationSpeed switch
    {
        ShellAnimationSpeed.Relaxed => 1.35,
        ShellAnimationSpeed.Fast => .72,
        _ => 1
    };

    private static Duration Timed(double milliseconds) =>
        new(TimeSpan.FromMilliseconds(Math.Max(1, milliseconds * SpeedFactor)));

    /// <summary>
    /// Attaches hover, press and focus motion to a tile. Safe to call repeatedly for the same
    /// button; handlers are only registered once.
    /// </summary>
    public static void Attach(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        if (GetAttached(button)) return;
        SetAttached(button, true);

        button.MouseEnter += (_, _) => Settle(button);
        button.MouseLeave += (_, _) => Settle(button);
        button.GotKeyboardFocus += (_, _) => Settle(button);
        button.LostKeyboardFocus += (_, _) => Settle(button);
        button.IsEnabledChanged += (_, _) => Settle(button);
        button.PreviewMouseLeftButtonDown += (_, _) => Press(button);
        button.PreviewMouseLeftButtonUp += (_, _) => Settle(button);
    }

    /// <summary>
    /// Drives a tile to whichever resting state its current hover/focus/enabled state implies.
    /// This is the only place that decides what "active" means, so hover and controller focus can
    /// never drift apart.
    /// </summary>
    public static void Settle(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        // Each toggle keeps its own meaning: "Tile hover effects" governs the mouse, "Tile focus
        // animation" governs controller/keyboard focus. A tile only rises for a state whose
        // setting is on, so turning one off never silently disables the other.
        var hovered = button.IsMouseOver && _settings.TileHoverEffectsEnabled;
        var focused = button.IsKeyboardFocused && _settings.TileFocusAnimationEnabled;
        var active = button.IsEnabled && (hovered || focused);
        Apply(button, active ? ActiveScale : RestScale, active ? ActiveLift : 0, active);
    }

    /// <summary>
    /// Momentary press-down. Used for mouse clicks and for controller Accept so a physical button
    /// press reads the same as a mouse press.
    /// </summary>
    public static void Press(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        if (!button.IsEnabled || !_settings.ButtonPressFeedbackEnabled) return;
        Apply(button, PressScale, 0, true, 90);
    }

    /// <summary>
    /// Press-and-release feedback for controller Accept, where there is no mouse-up to settle on.
    /// </summary>
    public static void Pulse(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        if (!button.IsEnabled) return;
        Press(button);
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(110 * SpeedFactor)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Settle(button);
        };
        timer.Start();
    }

    /// <summary>
    /// Fades and lifts tiles into place in sequence when a carousel or grid is first populated.
    /// The stagger is capped so a long library does not take seconds to appear.
    /// </summary>
    public static void PlayReveal(IEnumerable<FrameworkElement> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (!_settings.TileRevealAnimationEnabled)
        {
            foreach (var tile in tiles)
            {
                // Turning the reveal off mid-flight must not strand a tile faded out or lifted
                // off its row, so the opacity and the rise are both stopped and reset.
                tile.BeginAnimation(UIElement.OpacityProperty, null);
                tile.ClearValue(UIElement.OpacityProperty);
                if (tile.RenderTransform is TransformGroup group) StopAndSet(group, RestScale, 0);
            }
            return;
        }

        var index = 0;
        foreach (var tile in tiles)
        {
            // A disabled tile is dimmed by the shared button style. Animating its opacity would
            // override that and leave it looking enabled, so it keeps its styled appearance and
            // only rises with the row.
            var animateOpacity = tile.IsEnabled;
            var delay = Math.Min(index * 45, 360);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            index++;

            var transform = EnsureTransform(tile);
            transform.Children[1].BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, Timed(300))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay * SpeedFactor),
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            });

            if (!animateOpacity) continue;

            // Opacity is driven to 0 locally first so the tile is not briefly visible during the
            // stagger delay, and both the animation and that local value are released once the
            // reveal finishes. Releasing matters: a held animation or a leftover local value would
            // outrank the style that dims a tile when it later becomes unavailable.
            var reveal = new DoubleAnimation(0, 1, Timed(260))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay * SpeedFactor),
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            reveal.Completed += (_, _) =>
            {
                tile.BeginAnimation(UIElement.OpacityProperty, null);
                tile.ClearValue(UIElement.OpacityProperty);
            };
            tile.Opacity = 0;
            tile.BeginAnimation(UIElement.OpacityProperty, reveal);
        }
    }

    private static void Apply(Button button, double scale, double lift, bool glow, double? durationOverride = null)
    {
        var duration = Timed(durationOverride ?? 150);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (!_settings.TileHoverEffectsEnabled && !_settings.TileFocusAnimationEnabled)
        {
            // All tile motion is off, but focus must still be visible. The XAML focus border
            // carries that, so the tile is returned to rest with no animation and no residual
            // effect rather than being left mid-transform.
            StopAndSet(EnsureTransform(button), RestScale, 0);
            button.Effect = null;
            return;
        }

        var transform = EnsureTransform(button);
        var group = (ScaleTransform)transform.Children[0];
        var translate = (TranslateTransform)transform.Children[1];
        group.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        group.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(lift, duration) { EasingFunction = easing });

        if (glow)
        {
            if (button.Effect is not DropShadowEffect effect)
            {
                effect = new DropShadowEffect
                {
                    Color = GlowColor,
                    ShadowDepth = 0,
                    BlurRadius = 0,
                    Opacity = 0,
                    RenderingBias = RenderingBias.Performance
                };
                button.Effect = effect;
            }
            effect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(GlowBlur, duration) { EasingFunction = easing });
            effect.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(GlowOpacity, duration) { EasingFunction = easing });
            return;
        }

        if (button.Effect is not DropShadowEffect resting) return;
        // Clearing the effect entirely once faded keeps idle tiles off the effect rendering path
        // instead of leaving every tile in a carousel carrying a zero-opacity shadow.
        var fade = new DoubleAnimation(0, duration) { EasingFunction = easing };
        fade.Completed += (_, _) =>
        {
            if (button.IsMouseOver || button.IsKeyboardFocused) return;
            resting.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            resting.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            button.Effect = null;
        };
        resting.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(0, duration) { EasingFunction = easing });
        resting.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
    }

    private static void StopAndSet(TransformGroup transform, double scale, double lift)
    {
        // Tolerate a transform this helper did not build: a tile whose transform came from
        // somewhere else is simply left alone rather than crashing the shell on a cast.
        if (transform.Children.Count != 2 ||
            transform.Children[0] is not ScaleTransform group ||
            transform.Children[1] is not TranslateTransform translate)
        {
            return;
        }

        group.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        group.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        group.ScaleX = group.ScaleY = scale;
        translate.Y = lift;
    }

    private static TransformGroup EnsureTransform(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup existing &&
            existing.Children.Count == 2 &&
            existing.Children[0] is ScaleTransform &&
            existing.Children[1] is TranslateTransform)
        {
            return existing;
        }

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1, 1));
        group.Children.Add(new TranslateTransform(0, 0));
        element.RenderTransform = group;
        element.RenderTransformOrigin = new Point(.5, .5);
        return group;
    }

    private static bool GetAttached(DependencyObject element) =>
        element.GetValue(AttachedProperty) is true;

    private static void SetAttached(DependencyObject element, bool value) =>
        element.SetValue(AttachedProperty, value);

    private static readonly DependencyProperty AttachedProperty = DependencyProperty.RegisterAttached(
        "ShellTileMotionAttached",
        typeof(bool),
        typeof(ShellTileMotion),
        new PropertyMetadata(false));
}
