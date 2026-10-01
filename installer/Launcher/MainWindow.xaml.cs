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
    private readonly bool _updateMode;
    private int _page;
    private bool _installing;
    private bool _installed;
    private string _gamesPath = @"C:\GrevCo\GrevHome\Games";
    private string _biosPath = @"C:\GrevCo\GrevHome\bios";
    private ushort _previousButtons;
    private RetryMode _retry;
    private Action<string>? _folderPicked;
    private string? _folderPickerPath;

    private enum RetryMode { None, Install, Update }

    private const string DefaultInstallDirectory = @"C:\GrevCo\GrevHome";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{EA4D6CAE-5909-4557-9BC2-0C9B89151999}_is1";
    private DateTime _lastControllerAction = DateTime.MinValue;

    public MainWindow()
    {
        _updateMode = Environment.GetCommandLineArgs()
            .Skip(1)
            .Any(argument => string.Equals(argument, "--update", StringComparison.OrdinalIgnoreCase));
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
        SetupVersionText.Text = $"{(_updateMode ? "UPDATE" : "SETUP")}  ·  {version}";
        _pages = [WelcomePage, IntentPage, ConsolesPage, FoldersPage, ReadyPage, InstallPage];
        _steps = [Step1, Step2, Step3, Step4, Step5];
        _controllerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        _controllerTimer.Tick += PollController;
        _controllerTimer.Start();
        InstallLocationText.Text = ResolveInstallDirectory();
        Loaded += (_, _) =>
        {
            AnimateHero();
            if (_updateMode)
            {
                _ = RunUpdateAsync();
            }
            else
            {
                NextButton.Focus();
            }
        };
    }

    private async Task RunUpdateAsync()
    {
        ShowPage(5);
        InstallTitle.Text = "Updating Grev Home";
        InstallStatus.Text = "Waiting for Grev Home to close safely";
        try
        {
            await WaitForGrevHomeToExitAsync(TimeSpan.FromSeconds(30));
            await RunInstallerAsync(skipRunningAppCheck: true);
            if (!_installed) return;

            var app = Path.Combine(ResolveInstallDirectory(), "GrevHome.exe");
            if (!File.Exists(app))
            {
                throw new FileNotFoundException("The update completed, but Grev Home could not be found.", app);
            }

            Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            _installing = false;
            InstallTitle.Text = "Update needs your attention.";
            InstallStatus.Text = ex.Message;
            InstallPercent.Text = "";
            NavigationBar.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Collapsed;
            NextButton.Content = "Try again";
            _retry = RetryMode.Update;
            NextButton.Focus();
        }
    }

    private static async Task WaitForGrevHomeToExitAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Process.GetProcessesByName("GrevHome").Any(process => !process.HasExited))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException("Grev Home did not close in time. Close it completely, then select Try again.");
            }

            await Task.Delay(200);
        }
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
        HideNotice();
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
        if (_retry != RetryMode.None)
        {
            var retry = _retry;
            _retry = RetryMode.None;
            if (retry == RetryMode.Update) _ = RunUpdateAsync();
            else ShowPage(4);
            return;
        }
        if (_page == 0) { ShowPage(1); return; }
        if (_page == 1)
        {
            if (PcGamesCheck.IsChecked != true && AppsCheck.IsChecked != true && EmulatorsCheck.IsChecked != true)
            {
                ShowNotice("Choose at least one thing you want Grev Home to handle.");
                return;
            }
            ShowPage(EmulatorsCheck.IsChecked == true ? 2 : 3); return;
        }
        if (_page == 2)
        {
            if (!ConsoleChecks().Any(x => x.IsChecked == true))
            {
                ShowNotice("Choose at least one system, or go back and turn off Emulators.");
                return;
            }
            ShowPage(3); return;
        }
        if (_page == 3)
        {
            if ((GamesExisting.IsChecked == true && !Directory.Exists(_gamesPath)) ||
                (EmulatorsCheck.IsChecked == true && BiosExisting.IsChecked == true && !Directory.Exists(_biosPath)))
            {
                ShowNotice("Choose existing folders for the options you picked, or switch back to the Grev Home standard.");
                return;
            }
            ReadySelectionText.Text = string.Join(" · ", SelectedUsage());
            ShowPage(4); return;
        }
        if (_page == 4) { _ = RunInstallerAsync(); return; }
        if (_page == 5 && _installed)
        {
            var app = Path.Combine(ResolveInstallDirectory(), "GrevHome.exe");
            if (!File.Exists(app))
            {
                ShowNotice($"Grev Home was installed, but GrevHome.exe was not found in {Path.GetDirectoryName(app)}.");
                return;
            }
            Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
            Close();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        if (FolderPicker.Visibility == Visibility.Visible) { CloseFolderPicker(); return; }
        // Leaving a failed install screen cancels its pending retry, so the next Install press
        // really installs instead of only redrawing the page.
        _retry = RetryMode.None;
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

    private void BrowseGames_Click(object sender, RoutedEventArgs e) =>
        OpenFolderPicker("Choose your Games folder", _gamesPath, path => { _gamesPath = path; GamesPathText.Text = path; });

    private void BrowseBios_Click(object sender, RoutedEventArgs e) =>
        OpenFolderPicker("Choose your BIOS folder", _biosPath, path => { _biosPath = path; BiosPathText.Text = path; });

    // A controller-first folder browser. The Windows folder dialog cannot be driven by a gamepad,
    // so drives and folders are shown as ordinary focusable buttons instead.
    private void OpenFolderPicker(string title, string startPath, Action<string> picked)
    {
        _folderPicked = picked;
        FolderPickerTitle.Text = title;
        PageHost.IsEnabled = false;
        NavigationBar.IsEnabled = false;
        FolderPicker.Visibility = Visibility.Visible;
        ShowFolder(Directory.Exists(startPath) ? startPath : null);
    }

    private void ShowFolder(string? path)
    {
        _folderPickerPath = path;
        FolderList.Children.Clear();
        FolderPickerPath.Text = path ?? "Choose a drive";
        FolderUseButton.IsEnabled = path is not null;
        FolderUpButton.IsEnabled = path is not null;

        List<(string Label, string Target)> entries;
        try
        {
            entries = path is null
                ? DriveInfo.GetDrives()
                    .Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network)
                    .Select(drive => (string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? drive.Name.TrimEnd('\\')
                        : $"{drive.Name.TrimEnd('\\')}  {drive.VolumeLabel}", drive.RootDirectory.FullName))
                    .ToList()
                : new DirectoryInfo(path).EnumerateDirectories()
                    .Where(directory => (directory.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(400)
                    .Select(directory => (directory.Name, directory.FullName))
                    .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            entries = [];
            FolderPickerPath.Text = $"{path} – this folder cannot be opened.";
        }

        foreach (var (label, target) in entries)
        {
            var button = new Button
            {
                Content = label,
                Style = (Style)FindResource("QuietButton"),
                Margin = new Thickness(0, 0, 10, 10),
                MinWidth = 160
            };
            button.Click += (_, _) => ShowFolder(target);
            FolderList.Children.Add(button);
        }
        if (path is not null && entries.Count == 0 && FolderPickerPath.Text == path)
        {
            FolderPickerPath.Text = $"{path} – no folders inside. Choose Use this folder or go up a level.";
        }

        FolderListScroll.ScrollToTop();
        Dispatcher.InvokeAsync(() =>
        {
            if (FolderList.Children.Count > 0) ((UIElement)FolderList.Children[0]).Focus();
            else if (FolderUseButton.IsEnabled) FolderUseButton.Focus();
            else FolderCancelButton.Focus();
        }, DispatcherPriority.Input);
    }

    private void FolderUp_Click(object sender, RoutedEventArgs e)
    {
        if (_folderPickerPath is null) return;
        ShowFolder(Directory.GetParent(_folderPickerPath)?.FullName);
    }

    private void FolderUse_Click(object sender, RoutedEventArgs e)
    {
        if (_folderPickerPath is { } path && Directory.Exists(path)) _folderPicked?.Invoke(path);
        CloseFolderPicker();
    }

    private void FolderCancel_Click(object sender, RoutedEventArgs e) => CloseFolderPicker();

    private void CloseFolderPicker()
    {
        FolderPicker.Visibility = Visibility.Collapsed;
        PageHost.IsEnabled = true;
        NavigationBar.IsEnabled = true;
        _folderPicked = null;
        FocusPageStart();
    }

    private void ShowNotice(string message)
    {
        NoticeText.Text = message;
        NoticeText.Visibility = Visibility.Visible;
    }

    private void HideNotice()
    {
        NoticeText.Text = string.Empty;
        NoticeText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Where Grev Home is actually installed. Inno Setup reuses an existing install's folder on
    /// upgrade and records it under the uninstall key, so launching or updating must read it
    /// rather than assume the default.
    /// </summary>
    private static string ResolveInstallDirectory()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = root.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
                {
                    return location.TrimEnd('\\');
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }
        return DefaultInstallDirectory;
    }

    private async Task RunInstallerAsync(bool skipRunningAppCheck = false)
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
            if (!skipRunningAppCheck && Process.GetProcessesByName("GrevHome").Any(process => !process.HasExited))
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
                $"/UPDATE={(_updateMode ? 1 : 0)}",
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
            InstallStatus.Text = $"Installed at {ResolveInstallDirectory()}";
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
            BackButton.Visibility = _updateMode ? Visibility.Collapsed : Visibility.Visible;
            NextButton.Content = "Try again";
            _retry = _updateMode ? RetryMode.Update : RetryMode.Install;
            NextButton.Focus();
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_installing && (_page > 0 || FolderPicker.Visibility == Visibility.Visible)) { Back_Click(sender, e); e.Handled = true; }
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
            InstallStatus.Text = "Please let Grev Home finish installing before closing this window.";
            e.Cancel = true;
        }
        base.OnClosing(e);
    }

    private void PollController(object? sender, EventArgs e)
    {
        // Any connected pad drives the installer: Windows does not always put a single controller
        // in slot 0 (after a reconnect, or with another pad already paired).
        ushort logicalButtons = 0;
        try
        {
            for (uint slot = 0; slot < 4; slot++)
            {
                if (XInputGetState(slot, out var state) != 0) continue;
                logicalButtons |= state.Gamepad.Buttons;
                if (state.Gamepad.ThumbLY > 16000) logicalButtons |= DPadUp;
                else if (state.Gamepad.ThumbLY < -16000) logicalButtons |= DPadDown;
                if (state.Gamepad.ThumbLX < -16000) logicalButtons |= DPadLeft;
                else if (state.Gamepad.ThumbLX > 16000) logicalButtons |= DPadRight;
            }
        }
        catch (DllNotFoundException) { _controllerTimer.Stop(); return; }
        var pressed = (ushort)(logicalButtons & ~_previousButtons);
        _previousButtons = logicalButtons;
        if (pressed == 0 || DateTime.UtcNow - _lastControllerAction < TimeSpan.FromMilliseconds(115)) return;
        _lastControllerAction = DateTime.UtcNow;

        if ((pressed & (DPadUp | DPadLeft)) != 0) MoveFocus(FocusNavigationDirection.Previous);
        else if ((pressed & (DPadDown | DPadRight)) != 0) MoveFocus(FocusNavigationDirection.Next);
        else if ((pressed & ButtonA) != 0) ActivateFocusedControl();
        else if ((pressed & ButtonB) != 0 && !_installing && (_page > 0 || FolderPicker.Visibility == Visibility.Visible)) Back_Click(this, new RoutedEventArgs());
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
