using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using GrevHome.Input;
using GrevHome.Dashboard;
using GrevHome.Online;
using GrevHome.Presentation;
using GrevHome.Profiles;
using GrevHome.Sessions;
using GrevHome.Storage;
using GrevHome.Views;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Load only the app's styles, never its real startup/lifecycle handler.
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source=XDocument.Load("src/GrevHome/App.xaml");
        var resources=new XElement(presentation+"ResourceDictionary",
            new XAttribute(XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml"),
            source.Root!.Element(presentation+"Application.Resources")!.Nodes());
        app.Resources=(ResourceDictionary)XamlReader.Parse(resources.ToString());
        var login=new LoginView();
        var window=new Window {Content=login,Width=1280,Height=720,WindowStyle=WindowStyle.None};
        window.Show();
        var profiles=Enumerable.Range(1,8).Select(i=>new LocalProfile("GTEST"+i,"Player"+i,"Player "+i,DateTimeOffset.UtcNow,AccountRole.Standard)).ToArray();
        login.Refresh(profiles.Take(4).ToArray(),new SessionContext(),[true,false,false,false]);
        window.UpdateLayout();Pump();
        var scroll=(ScrollViewer)login.FindName("ProfilesScroll");
        var createAccount=(Button)login.FindName("CreateAccountButton");
        Check(createAccount.IsVisible,"Who's Playing must always offer Create Account before a session starts");
        Check(scroll.ScrollableWidth<1,"Four profiles must fit without horizontal scrolling at 720p");
        login.Refresh(profiles,new SessionContext(),[true,false,false,false]);
        window.UpdateLayout();Pump();
        Check(scroll.ScrollableWidth>0,"Additional profiles must scroll horizontally");
        var cards=login.ProfileFocusTargets;
        Check(cards.Count==8,"All offscreen profiles must remain controller reachable");
        cards[0].Focus();Pump();
        for(var i=0;i<7;i++){Check(login.MoveProfileFocus(InputAction.Right,cards[i]),"Right navigation must be handled");Pump();}
        Check(scroll.HorizontalOffset>0,"Controller focus must scroll to the offscreen profile");
        Check(login.MoveProfileFocus(InputAction.Right,cards[7]),"Right edge must remain contained");
        var keyboard=new ControllerQwertyKeyboard();
        keyboard.Open("Password","secret",256,true);
        var value=(TextBlock)keyboard.FindName("ValueText");
        Check(!value.Text.Contains("secret"),"Password text must be masked");
        keyboard.Cancel();Check(keyboard.Value==string.Empty,"Password keyboard must clear on close");

        var dashboard=new DashboardView();
        window.Content=dashboard;
        window.UpdateLayout();Pump();
        var browserButton=(Button)dashboard.FindName("WebBrowserButton");
        var appsCarousel=FindParent<ScrollViewer>(browserButton);
        Check(appsCarousel is not null && appsCarousel.ScrollableWidth>0,"Dashboard app tiles must remain in one horizontally scrollable row");
        browserButton.Focus();Pump();
        Check(appsCarousel.HorizontalOffset>0,"Controller focus must carry the dashboard carousel indicator to an offscreen tile");
        Check(appsCarousel.OpacityMask is System.Windows.Media.LinearGradientBrush,"Scrollable dashboard rows must fade at their offscreen edges");
        var inactiveTile=(Button)dashboard.FindName("YourGamesButton");
        Check(inactiveTile.BorderThickness.Left==0,"Dashboard tiles must not show an idle grey outline");
        string? previewBackground=null;
        dashboard.BackgroundPreviewRequested+=path=>previewBackground=path;
        var previewPresentation=new GrevHome.Store.ResolvedAppPresentation("Test","#151923",null,"tile.png","hero.png",false);
        var previewActivity=new DashboardAppActivity("test","Test",10,1,DateTimeOffset.UtcNow,true,true,null,previewPresentation,null);
        dashboard.SetDashboardData(new DashboardDataSnapshot(10,1,1,previewActivity,[previewActivity]));
        var continueTile=(Button)dashboard.FindName("ContinueButton");
        continueTile.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseEnterEvent});Pump();
        Check(previewBackground=="hero.png","Dashboard focus must prefer app hero artwork over tile artwork");
        var friend=new GrevDadFriend("1","friend","Friend",true,DateTimeOffset.UtcNow,new GrevDadPresence("online","Online","game","Playing",null,DateTimeOffset.UtcNow));
        dashboard.SetFriends(true,[friend],false);
        var friendsPanel=(StackPanel)dashboard.FindName("FriendsPanel");
        Check(friendsPanel.Children.Count==1 && friendsPanel.Children[0] is Button,"Dashboard friends must render as selectable friend cards without a separate Open Friends tile");
        var systemCarousel=(ScrollViewer)dashboard.FindName("SystemCarousel");
        var dashboardPowerTile=(Button)dashboard.FindName("SettingsPowerButton");
        Check(systemCarousel.ScrollableWidth>0,"All settings shortcuts must appear in the dashboard System row");
        dashboardPowerTile.Focus();Pump();
        Check(systemCarousel.HorizontalOffset>0,"Controller focus must carry the System row to offscreen settings shortcuts");
        SettingsPage? requestedPage=null;
        dashboard.SettingsPageRequested+=page=>requestedPage=page;
        dashboardPowerTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(requestedPage==SettingsPage.Power,"A dashboard settings shortcut must open its matching dedicated page");

        var settings=new SettingsView();
        window.Width=1920;
        window.Content=settings;
        window.UpdateLayout();Pump();
        var settingsCarousel=(ScrollViewer)settings.FindName("SettingsHubCarousel");
        var powerTile=(Button)settings.FindName("PowerSectionButton");
        var previewTitle=(TextBlock)settings.FindName("SettingsPreviewTitle");
        var previewItems=(ItemsControl)settings.FindName("SettingsPreviewItems");
        Check(settingsCarousel.ActualWidth>=window.ActualWidth-150,"The settings hub must use the same full-width dashboard geometry");
        Check(settingsCarousel.ScrollableWidth>0,"Every settings area must remain a tile in one horizontal hub row");
        powerTile.Focus();Pump();
        Check(settingsCarousel.HorizontalOffset>0,"Settings tile focus must scroll the hub carousel");
        powerTile.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseMoveEvent});Pump();
        Check(previewTitle.Text.Contains("Power") && previewItems.Items.Count==4,"Controller focus must preview every setting inside the highlighted tile");
        powerTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Check(((FrameworkElement)settings.FindName("PowerSection")).IsVisible,"A settings tile must open its dedicated settings page");
        Check(settings.TryReturnToSettingsHub(),"Back from a settings page must return to the tile hub first");Pump();

        var themeTile=(Button)settings.FindName("ThemeMotionSectionButton");
        themeTile.Focus();Pump();
        Check(previewTitle.Text.Contains("Theme") && previewItems.Items.Count==9,"Theme focus must preview every presentation control group");
        themeTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Check(((FrameworkElement)settings.FindName("ThemeMotionSection")).IsVisible,"Theme & Motion must have a dedicated controller page");
        ShellMotionSettings? changedMotion=null;
        settings.MotionSettingsChanged+=value=>changedMotion=value;
        ((Button)settings.FindName("ScreenTransitionsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(changedMotion?.ScreenTransitionsEnabled==false,"Screen transitions must be switchable from the controller settings page");
        ((Button)settings.FindName("OverlayTransitionsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(changedMotion?.OverlayTransitionsEnabled==false,"Overlay transitions must be switchable by controller");
        ((Button)settings.FindName("ControllerVibrationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(changedMotion?.ControllerVibrationEnabled==false,"Controller vibration must be independently switchable");
        ((Button)settings.FindName("AnimationSpeedButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(changedMotion?.AnimationSpeed==ShellAnimationSpeed.Fast,"Animation speed must cycle from Normal to Fast");

        var motionRoot=Path.Combine(Path.GetTempPath(),"GrevHomeMotionTest-"+Guid.NewGuid().ToString("N"));
        try
        {
            var motionService=new ShellMotionSettingsService(new AppPaths(motionRoot));
            Check(motionService.Load()==new ShellMotionSettings(),"Motion settings must default to enabled");
            var savedMotion=new ShellMotionSettings(
                ScreenTransitionsEnabled:false,
                StartupIntroEnabled:false,
                OverlayTransitionsEnabled:false,
                ReturnHomeTransitionEnabled:false,
                TileFocusAnimationEnabled:false,
                ModalTransitionsEnabled:false,
                AmbientBackgroundEnabled:false,
                ButtonPressFeedbackEnabled:false,
                UiSoundsEnabled:false,
                StartupSoundEnabled:false,
                ControllerVibrationEnabled:false,
                UiSoundVolumePercent:75,
                AnimationSpeed:ShellAnimationSpeed.Fast,
                VibrationStrength:ShellVibrationStrength.High);
            motionService.SaveAsync(savedMotion).GetAwaiter().GetResult();
            Check(motionService.Load()==savedMotion,"Motion settings must survive an application restart");
        }
        finally
        {
            if(Directory.Exists(motionRoot))Directory.Delete(motionRoot,true);
        }

        var guestRoot=Path.Combine(Path.GetTempPath(),"GrevHomeBuiltInGuestTest-"+Guid.NewGuid().ToString("N"));
        try
        {
            var profilesService=new ProfileService(new AppPaths(guestRoot));
            var builtInGuest=profilesService.EnsureBuiltInGuestAsync().GetAwaiter().GetResult();
            Check(builtInGuest.IsBuiltInGuest && builtInGuest.Role==AccountRole.Guest && builtInGuest.DisplayName=="Guest","A fixed built-in Guest must exist on a new Grev Home system");
            var firstCreated=profilesService.CreateAsync("Owner",AccountRole.Standard).GetAwaiter().GetResult();
            Check(firstCreated.Role==AccountRole.Admin,"The built-in Guest must not prevent the first real account becoming Admin");
            var guestRenameBlocked=false;
            try { profilesService.UpdateDisplayNameAsync(builtInGuest.GrevId,"Renamed").GetAwaiter().GetResult(); }
            catch(InvalidOperationException) { guestRenameBlocked=true; }
            Check(guestRenameBlocked,"The built-in Guest identity must be immutable");
        }
        finally
        {
            if(Directory.Exists(guestRoot))Directory.Delete(guestRoot,true);
        }
        window.Close();
        Console.WriteLine("Carousel tests passed: profiles, dashboard and settings hubs, focus scrolling, edge fades and password masking.");
        app.Shutdown();
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static T? FindParent<T>(DependencyObject child) where T:DependencyObject
    {
        DependencyObject? current=child;
        while((current=System.Windows.Media.VisualTreeHelper.GetParent(current)) is not null)
            if(current is T match)return match;
        return null;
    }
}
