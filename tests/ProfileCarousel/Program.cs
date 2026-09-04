using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GrevHome.Input;
using GrevHome.Profiles;
using GrevHome.Sessions;
using GrevHome.Views;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app=new GrevHome.App();app.InitializeComponent();
        var login=new LoginView();
        var window=new Window {Content=login,Width=1280,Height=720,WindowStyle=WindowStyle.None};
        window.Show();
        var profiles=Enumerable.Range(1,8).Select(i=>new LocalProfile("GTEST"+i,"Player"+i,"Player "+i,DateTimeOffset.UtcNow,AccountRole.Standard)).ToArray();
        login.Refresh(profiles.Take(4).ToArray(),new SessionContext(),[true,false,false,false]);
        window.UpdateLayout();Pump();
        var scroll=(ScrollViewer)login.FindName("ProfilesScroll");
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
        window.Close();
        Console.WriteLine("Profile carousel tests passed: four-card layout, offscreen controller navigation and password masking.");
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
