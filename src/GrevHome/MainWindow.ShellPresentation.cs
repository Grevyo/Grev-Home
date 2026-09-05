using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GrevHome.Navigation;
using GrevHome.Presentation;

namespace GrevHome;

public partial class MainWindow
{
    private long _introAnimationVersion;

    private double MotionSpeedFactor => _shellMotionSettings.AnimationSpeed switch
    {
        ShellAnimationSpeed.Relaxed => 1.35,
        ShellAnimationSpeed.Fast => .72,
        _ => 1
    };

    private Duration MotionDuration(double milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds * MotionSpeedFactor);

    private void InitializePresentationEffects()
    {
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(PresentationFocusChanged), true);
        AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(PresentationFocusChanged), true);
        PowerMenuOverlay.IsVisibleChanged += (_, _) =>
        {
            if (PowerMenuOverlay.IsVisible) AnimateModalEntrance(PowerMenuCard.IsVisible ? PowerMenuCard : ProfileQuickMenuCard);
        };
        StoreModalOverlay.IsVisibleChanged += (_, _) =>
        {
            if (StoreModalOverlay.IsVisible && StoreModalOverlay.Child is FrameworkElement card) AnimateModalEntrance(card);
        };
        ApplyAmbientBackgroundSetting();
    }

    private async Task SaveMotionSettingsAsync(ShellMotionSettings settings)
    {
        try
        {
            await _shellMotionSettingsService.SaveAsync(settings);
            _shellMotionSettings = settings;
            _settingsView.SetMotionSettings(settings);
            _overlayWindow.ConfigurePresentation(settings);
            if (!settings.ScreenTransitionsEnabled) ResetRouteAnimation();
            ApplyAmbientBackgroundSetting();
            _settingsView.ShowMotionStatus("Theme and motion settings saved for this Grev Home machine.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _settingsView.SetMotionSettings(_shellMotionSettings);
            _settingsView.ShowMotionStatus($"Could not save theme and motion settings: {ex.Message}");
        }
    }

    private void AnimateRouteTransition(NavigationTransition? transition)
    {
        if (!_shellMotionSettings.ScreenTransitionsEnabled ||
            _startupIntroPlaying ||
            transition is null ||
            transition.Kind is NavigationTransitionKind.SameRoutePush or NavigationTransitionKind.SameRouteBack)
        {
            ResetRouteAnimation();
            return;
        }

        var from = transition.Kind == NavigationTransitionKind.Back ? -28d : 28d;
        var transform = RouteHost.RenderTransform as TranslateTransform ?? new TranslateTransform();
        RouteHost.RenderTransform = transform;
        RouteHost.RenderTransformOrigin = new Point(0.5, 0.5);

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        RouteHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.12, 1, MotionDuration(260)) { EasingFunction = easing });
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(from, 0, MotionDuration(300)) { EasingFunction = easing });
    }

    private void ResetRouteAnimation()
    {
        RouteHost.BeginAnimation(OpacityProperty, null);
        RouteHost.Opacity = 1;
        if (RouteHost.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
        }
    }

    private void BeginStartupIntro(bool force = false)
    {
        if (!force && !_shellMotionSettings.StartupIntroEnabled)
        {
            StartupIntroOverlay.Visibility = Visibility.Collapsed;
            ShellInteractionHost.IsEnabled = true;
            _startupIntroPlaying = false;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(FocusFirstButton));
            return;
        }

        var version = ++_introAnimationVersion;
        if (_shellMotionSettings.StartupSoundEnabled)
        {
            _shellFeedback.Play(ShellSound.Startup, _shellMotionSettings.UiSoundVolumePercent);
        }
        _startupIntroPlaying = true;
        ShellInteractionHost.IsEnabled = false;
        StartupIntroOverlay.Visibility = Visibility.Visible;
        StartupIntroOverlay.Opacity = 1;
        IntroOuterGlow.Opacity = 0;
        IntroGlowScale.ScaleX = IntroGlowScale.ScaleY = 0.25;
        IntroMark.Opacity = 0;
        IntroMarkScale.ScaleX = IntroMarkScale.ScaleY = 0.55;
        IntroTitleText.Opacity = 0;
        IntroTaglineText.Opacity = 0;
        IntroLightBar.Width = 0;

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        IntroOuterGlow.BeginAnimation(OpacityProperty,
            TimedAnimation(0, 0.9, 720, 80, easeOut));
        IntroGlowScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            TimedAnimation(0.25, 1, 820, 40, easeOut));
        IntroGlowScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            TimedAnimation(0.25, 1, 820, 40, easeOut));
        IntroMark.BeginAnimation(OpacityProperty,
            TimedAnimation(0, 1, 560, 360, easeOut));
        IntroMarkScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            TimedAnimation(0.55, 1, 650, 310, easeOut));
        IntroMarkScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            TimedAnimation(0.55, 1, 650, 310, easeOut));
        IntroTitleText.BeginAnimation(OpacityProperty,
            TimedAnimation(0, 1, 500, 880, easeOut));
        IntroLightBar.BeginAnimation(FrameworkElement.WidthProperty,
            TimedAnimation(0, 330, 620, 1040, easeOut));
        IntroTaglineText.BeginAnimation(OpacityProperty,
            TimedAnimation(0, 1, 460, 1370, easeOut));

        var finish = TimedAnimation(1, 0, 620, 2600, new QuadraticEase { EasingMode = EasingMode.EaseInOut });
        finish.Completed += (_, _) =>
        {
            if (version != _introAnimationVersion) return;
            StartupIntroOverlay.Visibility = Visibility.Collapsed;
            StartupIntroOverlay.BeginAnimation(OpacityProperty, null);
            StartupIntroOverlay.Opacity = 1;
            ShellInteractionHost.IsEnabled = true;
            _startupIntroPlaying = false;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(FocusFirstButton));
        };
        StartupIntroOverlay.BeginAnimation(OpacityProperty, finish);
    }

    private void AnimateReturnHome()
    {
        if (!_shellMotionSettings.ReturnHomeTransitionEnabled) return;
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MotionDuration(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (Keyboard.FocusedElement is Button button) PulseFocusedButton(button);
        }));
    }

    private void PresentationFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_shellMotionSettings.TileFocusAnimationEnabled) return;
        if (e.OldFocus is Button oldButton) AnimateButtonScale(oldButton, 1);
        if (e.NewFocus is Button newButton) AnimateButtonScale(newButton, 1.025);
    }

    private void AnimateButtonScale(Button button, double target)
    {
        if (button.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            button.RenderTransform = scale;
            button.RenderTransformOrigin = new Point(.5, .5);
        }
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, MotionDuration(130)) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, MotionDuration(130)) { EasingFunction = easing });
    }

    private void PulseFocusedButton(Button button)
    {
        if (!_shellMotionSettings.TileFocusAnimationEnabled) return;
        AnimateButtonScale(button, 1.045);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110 * MotionSpeedFactor) };
        timer.Tick += (_, _) => { timer.Stop(); AnimateButtonScale(button, 1.025); };
        timer.Start();
    }

    private void AnimateModalEntrance(FrameworkElement card)
    {
        if (!_shellMotionSettings.ModalTransitionsEnabled) return;
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MotionDuration(180)));
        var transform = card.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        card.RenderTransform = transform;
        card.RenderTransformOrigin = new Point(.5, .5);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.97, 1, MotionDuration(190)) { EasingFunction = easing });
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.97, 1, MotionDuration(190)) { EasingFunction = easing });
    }

    private void ApplyAmbientBackgroundSetting()
    {
        AmbientBackgroundLayer.Visibility = _shellMotionSettings.AmbientBackgroundEnabled ? Visibility.Visible : Visibility.Collapsed;
        AmbientGlowOneTransform.BeginAnimation(TranslateTransform.XProperty, null);
        AmbientGlowOneTransform.BeginAnimation(TranslateTransform.YProperty, null);
        AmbientGlowTwoTransform.BeginAnimation(TranslateTransform.XProperty, null);
        AmbientGlowTwoTransform.BeginAnimation(TranslateTransform.YProperty, null);
        if (!_shellMotionSettings.AmbientBackgroundEnabled) return;
        var duration = TimeSpan.FromSeconds(18 * MotionSpeedFactor);
        var drift = new SineEase { EasingMode = EasingMode.EaseInOut };
        AmbientGlowOneTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 150, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = drift });
        AmbientGlowOneTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 90, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = drift });
        AmbientGlowTwoTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -130, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = drift });
        AmbientGlowTwoTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -70, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = drift });
    }

    private void HandlePresentationInputFeedback(InputAction action, int? controllerIndex)
    {
        var sound = action switch
        {
            InputAction.Accept => ShellSound.Select,
            InputAction.Back => ShellSound.Back,
            _ => ShellSound.Navigate
        };
        if (_shellMotionSettings.UiSoundsEnabled) _shellFeedback.Play(sound, _shellMotionSettings.UiSoundVolumePercent);
        if (action == InputAction.Accept && _shellMotionSettings.ButtonPressFeedbackEnabled && Keyboard.FocusedElement is Button button) PulseFocusedButton(button);
        if (controllerIndex.HasValue && _shellMotionSettings.ControllerVibrationEnabled && action is InputAction.Accept or InputAction.Back)
        {
            var strength = _shellMotionSettings.VibrationStrength switch
            {
                ShellVibrationStrength.Medium => (ushort)10500,
                ShellVibrationStrength.High => (ushort)18000,
                _ => (ushort)5500
            };
            _controllerInput.PulseVibration(controllerIndex.Value, strength, action == InputAction.Accept ? 55 : 38);
        }
    }

    private DoubleAnimation TimedAnimation(
        double from,
        double to,
        int durationMilliseconds,
        int delayMilliseconds,
        IEasingFunction easing) =>
        new(from, to, MotionDuration(durationMilliseconds))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds * MotionSpeedFactor),
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };
}
