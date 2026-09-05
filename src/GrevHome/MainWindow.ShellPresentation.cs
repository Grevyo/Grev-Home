using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GrevHome.Navigation;
using GrevHome.Presentation;

namespace GrevHome;

public partial class MainWindow
{
    private long _introAnimationVersion;

    private async Task SaveMotionSettingsAsync(ShellMotionSettings settings)
    {
        try
        {
            await _shellMotionSettingsService.SaveAsync(settings);
            _shellMotionSettings = settings;
            _settingsView.SetMotionSettings(settings);
            if (!settings.ScreenTransitionsEnabled) ResetRouteAnimation();
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
            new DoubleAnimation(0.12, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = easing });
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

    private static DoubleAnimation TimedAnimation(
        double from,
        double to,
        int durationMilliseconds,
        int delayMilliseconds,
        IEasingFunction easing) =>
        new(from, to, TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };
}
