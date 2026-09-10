using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GrevHome.Installer;

public partial class MainWindow : Window
{
    private readonly Grid[] _pages;
    private readonly TextBlock[] _steps;
    private readonly DispatcherTimer _controllerTimer;
    private int _page;
    private bool _installing;
    private bool _installed;
    private string _gamesPath = @"C:\GrevCo\GrevHome\Games";
    private string _biosPath = @"C:\GrevCo\GrevHome\bios";
    private ushort _previousButtons;
    private DateTime _lastControllerAction = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _pages = [WelcomePage, IntentPage, ConsolesPage, FoldersPage, ReadyPage, InstallPage];
        _steps = [Step1, Step2, Step3, Step4, Step5];
        _controllerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        _controllerTimer.Tick += PollController;
        _controllerTimer.Start();
        Loaded += (_, _) =>
        {
            AnimateHero();
            NextButton.Focus();
        };
    }

    private void AnimateHero()
    {
        var pulse = new DoubleAnimation(0.96, 1.04, TimeSpan.FromSeconds(2.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
        HeroScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        HeroScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        HeroRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(-2, 2, TimeSpan.FromSeconds(4.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        ((TranslateTransform)AmbientPurple.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-35, 48, TimeSpan.FromSeconds(11)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        ((TranslateTransform)AmbientCyan.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-28, 30, TimeSpan.FromSeconds(9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
    }

    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, _pages.Length - 1);
        foreach (var item in _pages) item.Visibility = Visibility.Collapsed;
        var target = _pages[_page];
        target.Visibility = Visibility.Visible;
        target.Opacity = 0;
        target.RenderTransform = new TranslateTransform(22, 0);
        target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        ((TranslateTransform)target.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        var activeStep = _page switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, _ => 4 };
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(i == activeStep ? "#FFFFFF" : i < activeStep ? "#4ACFF3" : "#65718C"));
            _steps[i].FontWeight = i == activeStep ? FontWeights.SemiBold : FontWeights.Normal;
        }

        BackButton.Visibility = _page is > 0 and < 5 ? Visibility.Visible : Visibility.Collapsed;
        NavigationBar.Visibility = _page == 5 && !_installed ? Visibility.Collapsed : Visibility.Visible;
        ControllerHint.Visibility = _page == 5 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _page switch { 0 => "Get started", 4 => "Install Grev Home", 5 => "Launch Grev Home", _ => "Continue" };
        Dispatcher.InvokeAsync(FocusPageStart, DispatcherPriority.Input);
    }

    private void FocusPageStart()
    {
        var target = _page switch
        {
            1 => (UIElement)PcGamesCheck,
            2 => Ps1Check,
            3 => GamesStandard,
            _ => NextButton
        };
        target.Focus();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page == 0) { ShowPage(1); return; }
        if (_page == 1)
        {
            if (PcGamesCheck.IsChecked != true && AppsCheck.IsChecked != true && EmulatorsCheck.IsChecked != true)
            {
                MessageBox.Show(this, "Choose at least one thing you want Grev Home to handle.", "Choose your setup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowPage(EmulatorsCheck.IsChecked == true ? 2 : 3); return;
        }
        if (_page == 2)
        {
            if (!ConsoleChecks().Any(x => x.IsChecked == true))
            {
                MessageBox.Show(this, "Choose at least one system, or go back and turn off Emulators.", "Choose your systems", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowPage(3); return;
        }
        if (_page == 3)
        {
            if ((GamesExisting.IsChecked == true && !Directory.Exists(_gamesPath)) ||
                (EmulatorsCheck.IsChecked == true && BiosExisting.IsChecked == true && !Directory.Exists(_biosPath)))
            {
                MessageBox.Show(this, "Choose valid existing folders before continuing.", "Check your folders", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ReadySelectionText.Text = string.Join(" · ", SelectedUsage());
            ShowPage(4); return;
        }
        if (_page == 4) { _ = RunInstallerAsync(); return; }
        if (_page == 5 && _installed)
        {
            var app = @"C:\GrevCo\GrevHome\GrevHome.exe";
            if (File.Exists(app)) Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
            Close();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        if (_page == 3 && EmulatorsCheck.IsChecked != true) ShowPage(1);
        else ShowPage(_page - 1);
    }

    private IEnumerable<string> SelectedUsage()
    {
        if (PcGamesCheck.IsChecked == true) yield return "PC games";
        if (AppsCheck.IsChecked == true) yield return "Apps";
        if (EmulatorsCheck.IsChecked == true) yield return "Emulators";
    }

    private CheckBox[] ConsoleChecks() => [Ps1Check, Ps2Check, Ps3Check, DsCheck, SwitchCheck, ThreeDsCheck, XboxCheck];

    private void FolderChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        BrowseGamesButton.Visibility = GamesExisting.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        BrowseBiosButton.Visibility = BiosExisting.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        GamesStandardNotice.Visibility = GamesStandard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        BiosStandardNotice.Visibility = BiosStandard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (GamesStandard.IsChecked == true) _gamesPath = @"C:\GrevCo\GrevHome\Games";
        if (BiosStandard.IsChecked == true) _biosPath = @"C:\GrevCo\GrevHome\bios";
        GamesPathText.Text = _gamesPath;
        BiosPathText.Text = _biosPath;
    }

    private void BrowseGames_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose your existing Games folder", InitialDirectory = Directory.Exists(_gamesPath) ? _gamesPath : null };
        if (dialog.ShowDialog(this) == true) { _gamesPath = dialog.FolderName; GamesPathText.Text = _gamesPath; }
    }

    private void BrowseBios_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose your existing BIOS folder", InitialDirectory = Directory.Exists(_biosPath) ? _biosPath : null };
        if (dialog.ShowDialog(this) == true) { _biosPath = dialog.FolderName; BiosPathText.Text = _biosPath; }
    }

    private async Task RunInstallerAsync()
    {
        _installing = true;
        ShowPage(5);
        var progress = 3d;
        var progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        progressTimer.Tick += (_, _) =>
        {
            progress = Math.Min(92, progress + Math.Max(0.15, (94 - progress) / 90));
            InstallProgress.Value = progress;
            InstallPercent.Text = $"{progress:0}%";
            InstallStatus.Text = progress < 25 ? "Preparing the Grev Home engine" : progress < 72 ? "Installing Grev Home and browser components" : "Finishing your setup";
        };
        progressTimer.Start();

        string? tempRoot = null;
        try
        {
            if (Process.GetProcessesByName("GrevHome").Any(process => !process.HasExited))
            {
                throw new InvalidOperationException("Grev Home is still running. Close it completely, then select Try again.");
            }

            tempRoot = Path.Combine(Path.GetTempPath(), "GrevHomeInstaller", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var enginePath = Path.Combine(tempRoot, "GrevHomeSetupEngine.exe");
            var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Grev Home", "Installer Logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, $"setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            await ExtractEngineAsync(enginePath);

            var selectedConsoles = string.Join('|', ConsoleChecks().Where(x => x.IsChecked == true).Select(x => x.Content?.ToString()));
            var start = new ProcessStartInfo(enginePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempRoot
            };
            foreach (var argument in new[]
            {
                "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", $"/LOG={logPath}",
                $"/GAMESROOT={_gamesPath}", $"/BIOSROOT={_biosPath}",
                $"/PCGAMES={(PcGamesCheck.IsChecked == true ? 1 : 0)}",
                $"/APPS={(AppsCheck.IsChecked == true ? 1 : 0)}",
                $"/EMULATORS={(EmulatorsCheck.IsChecked == true ? 1 : 0)}",
                $"/CONSOLES={selectedConsoles}"
            }) start.ArgumentList.Add(argument);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("The setup engine could not start.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException(BuildSetupError(process.ExitCode, logPath));

            progressTimer.Stop();
            InstallProgress.Value = 100;
            InstallPercent.Text = "100%";
            InstallTitle.Text = "Your Grev Home is ready.";
            InstallStatus.Text = "Installed at C:\\GrevCo\\GrevHome";
            _installed = true;
            _installing = false;
            NavigationBar.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Collapsed;
            NextButton.Content = "Launch Grev Home";
            NextButton.Focus();
        }
        catch (Exception ex)
        {
            progressTimer.Stop();
            _installing = false;
            InstallTitle.Text = "Setup needs your attention.";
            InstallStatus.Text = ex.Message;
            InstallPercent.Text = "";
            NavigationBar.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Visible;
            NextButton.Content = "Try again";
            NextButton.Click -= Next_Click;
            NextButton.Click += Retry_Click;
        }
        finally
        {
            if (tempRoot is not null)
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    private static string BuildSetupError(int exitCode, string logPath)
    {
        var detail = "";
        try
        {
            detail = File.ReadLines(logPath)
                .Reverse()
                .FirstOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                        line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                                        line.Contains("mutex", StringComparison.OrdinalIgnoreCase) ||
                                        line.Contains("access is denied", StringComparison.OrdinalIgnoreCase))
                ?.Trim() ?? "";
        }
        catch { }

        if (detail.Length > 220) detail = detail[..220] + "…";
        var message = exitCode switch
        {
            2 => "Setup was cancelled.",
            5 => "Setup completed, but Windows needs to restart.",
            _ => $"The setup engine stopped with code {exitCode}."
        };
        if (!string.IsNullOrWhiteSpace(detail)) message += $" {detail}";
        return $"{message}\nLog saved to: {logPath}";
    }

    private async Task ExtractEngineAsync(string destination)
    {
        await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("GrevHomeSetupEngine.exe")
            ?? throw new InvalidOperationException("The embedded setup engine is missing. Download a complete Grev Home installer.");
        await using var file = File.Create(destination);
        await resource.CopyToAsync(file);
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        NextButton.Click -= Retry_Click;
        NextButton.Click += Next_Click;
        ShowPage(4);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_installing) { Back_Click(sender, e); e.Handled = true; }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_installing)
        {
            MessageBox.Show(this, "Please let Grev Home finish installing before closing this window.", "Installation in progress", MessageBoxButton.OK, MessageBoxImage.Information);
            e.Cancel = true;
        }
        base.OnClosing(e);
    }

    private void PollController(object? sender, EventArgs e)
    {
        XInputState state;
        try { if (XInputGetState(0, out state) != 0) { _previousButtons = 0; return; } }
        catch (DllNotFoundException) { _controllerTimer.Stop(); return; }
        var logicalButtons = state.Gamepad.Buttons;
        if (state.Gamepad.ThumbLY > 16000) logicalButtons |= DPadUp;
        else if (state.Gamepad.ThumbLY < -16000) logicalButtons |= DPadDown;
        if (state.Gamepad.ThumbLX < -16000) logicalButtons |= DPadLeft;
        else if (state.Gamepad.ThumbLX > 16000) logicalButtons |= DPadRight;
        var pressed = (ushort)(logicalButtons & ~_previousButtons);
        _previousButtons = logicalButtons;
        if (pressed == 0 || DateTime.UtcNow - _lastControllerAction < TimeSpan.FromMilliseconds(115)) return;
        _lastControllerAction = DateTime.UtcNow;

        if ((pressed & (DPadUp | DPadLeft)) != 0) MoveFocus(FocusNavigationDirection.Previous);
        else if ((pressed & (DPadDown | DPadRight)) != 0) MoveFocus(FocusNavigationDirection.Next);
        else if ((pressed & ButtonA) != 0) ActivateFocusedControl();
        else if ((pressed & ButtonB) != 0 && !_installing && _page > 0) Back_Click(this, new RoutedEventArgs());
    }

    private static void MoveFocus(FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is UIElement focused) focused.MoveFocus(new TraversalRequest(direction));
    }

    private static void ActivateFocusedControl()
    {
        if (Keyboard.FocusedElement is ToggleButton toggle) toggle.IsChecked = toggle is RadioButton ? true : toggle.IsChecked != true;
        else if (Keyboard.FocusedElement is Button button) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008, ButtonA = 0x1000, ButtonB = 0x2000;
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);
    [StructLayout(LayoutKind.Sequential)] private struct XInputState { public uint PacketNumber; public XInputGamepad Gamepad; }
    [StructLayout(LayoutKind.Sequential)] private struct XInputGamepad { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short ThumbLX, ThumbLY, ThumbRX, ThumbRY; }
}
