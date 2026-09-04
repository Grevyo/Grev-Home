using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GrevHome.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GrevHome.Views;

public partial class GrevDadWebView : UserControl, IDisposable
{
    private WebView2? _browser;
    private Uri _home=new("https://grev.dad/");
    private bool _browsing;
    private bool _busy;
    private int _generation;
    public bool OwnsControllerInput => _browsing || ChoicesOverlay.Visibility==Visibility.Visible;
    public event EventHandler? ExitRequested;
    public GrevDadWebView()
    {
        InitializeComponent();
        KeyboardOverlay.Completed += value => _ = SetFieldAsync(value);
        KeyboardOverlay.Cancelled += (_,_) => ShowBrowser();
        IsEnabledChanged+=(_,_)=>
        {
            if(_browser is not null) _browser.Visibility=IsEnabled && !KeyboardOverlay.IsOpen && ChoicesOverlay.Visibility!=Visibility.Visible ? Visibility.Visible : Visibility.Hidden;
        };
    }

    public async Task OpenAsync(string userDataFolder, Uri home, Uri target)
    {
        DisposeBrowser();
        _home=home;
        if (!IsAllowed(target.AbsoluteUri)) { HintText.Text="Only your configured Grev.dad website can open here."; return; }
        var generation=_generation;
        var browser=new WebView2 { DefaultBackgroundColor=System.Drawing.Color.FromArgb(16,21,31),Focusable=false };
        _browser=browser;
        BrowserHost.Children.Add(browser);
        HintText.Text="Opening Grev.dad…";
        try
        {
            var environment=await CoreWebView2Environment.CreateAsync(userDataFolder:userDataFolder);
            if(generation!=_generation) return;
            await browser.EnsureCoreWebView2Async(environment);
            if(generation!=_generation) return;
            var core=browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled=false;
            core.Settings.AreDefaultContextMenusEnabled=false;
            core.Settings.IsPasswordAutosaveEnabled=false;
            core.Settings.IsGeneralAutofillEnabled=false;
            core.PermissionRequested += (_,e)=>e.State=CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_,e)=> {e.Cancel=true; HintText.Text="Downloads are not enabled in this account browser.";};
            core.NavigationStarting += (_,e)=>
            {
                if(!IsAllowed(e.Uri)) { e.Cancel=true; HintText.Text="External websites stay outside the Grev.dad account browser."; }
                else { _browsing=false; BrowseButton.Focus(); }
            };
            core.NewWindowRequested += (_,e)=> { e.Handled=true; if(IsAllowed(e.Uri)) core.Navigate(e.Uri); };
            core.NavigationCompleted += async (_,e)=>
            {
                if(generation!=_generation) return;
                if(!e.IsSuccess) {HintText.Text="Grev.dad could not load. Check your connection, then choose Reload or Back.";return;}
                try { await core.ExecuteScriptAsync(ControllerScript); HintText.Text="Browse page: D-pad selects links/fields • A opens or types • B returns to browser controls"; }
                catch (Exception ex) when(ex is InvalidOperationException or System.Runtime.InteropServices.COMException) {HintText.Text="Choose Reload to reconnect the browser.";}
            };
            core.Navigate(target.AbsoluteUri);
        }
        catch(Exception ex) when(ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            if(generation==_generation) HintText.Text="The Grev.dad browser could not start. Microsoft Edge WebView2 Runtime must be installed. You can still approve the copied code on another device.";
        }
    }

    private bool IsAllowed(string uri) => Uri.TryCreate(uri,UriKind.Absolute,out var target) && target.Scheme=="https" &&
        string.Equals(target.Host,_home.Host,StringComparison.OrdinalIgnoreCase) && target.Port==_home.Port;

    public bool HandleInput(InputAction action)
    {
        if(ChoicesOverlay.Visibility==Visibility.Visible)
        {
            if(action==InputAction.Back) { ChoicesOverlay.Visibility=Visibility.Collapsed;ShowBrowser();return true; }
            return false;
        }
        if(!_browsing) return false;
        if(action==InputAction.Back) { _browsing=false;BrowseButton.Focus();return true; }
        if(action==InputAction.Accept) _=ActivateAsync();
        else _=RunAsync($"window.grevController?.move({(action is InputAction.Left or InputAction.Up ? -1:1)})");
        return true;
    }

    private async Task<string?> RunAsync(string script)
    {
        var browser=_browser;
        if(browser?.CoreWebView2 is null || !IsAllowed(browser.Source?.AbsoluteUri??"")) return null;
        try { return await browser.ExecuteScriptAsync(script); }
        catch(Exception ex) when(ex is InvalidOperationException or System.Runtime.InteropServices.COMException) {return null;}
    }

    private async Task ActivateAsync()
    {
        if(_busy) return;
        _busy=true;
        var generation=_generation;
        try
        {
            var response=await RunAsync("window.grevController?.activate()");
            if(generation!=_generation || !IsVisible || response is null or "null") return;
            using var json=JsonDocument.Parse(response);
            if(!json.RootElement.TryGetProperty("kind",out var kind)) return;
            if(kind.GetString()=="text")
            {
                if(_browser is not null) _browser.Visibility=Visibility.Hidden;
                KeyboardOverlay.Open("Enter website text",string.Empty,256,json.RootElement.GetProperty("password").GetBoolean());
            }
            else if(kind.GetString()=="select")
            {
                if(_browser is not null) _browser.Visibility=Visibility.Hidden;
                ChoicesPanel.Children.Clear();
                foreach(var option in json.RootElement.GetProperty("options").EnumerateArray())
                {
                    var value=option.GetProperty("value").GetString()??"";
                    var button=new Button {Content=option.GetProperty("text").GetString(),Margin=new Thickness(4),Padding=new Thickness(14),
                        IsEnabled=!option.GetProperty("disabled").GetBoolean()};
                    button.Click+=(_,_)=>{ChoicesOverlay.Visibility=Visibility.Collapsed;_=SetFieldAsync(value);};
                    ChoicesPanel.Children.Add(button);
                }
                ChoicesOverlay.Visibility=Visibility.Visible;
                ChoicesPanel.Children.OfType<Button>().FirstOrDefault(b=>b.IsEnabled)?.Focus();
            }
        }
        catch(JsonException) { HintText.Text="This control is not available. Choose another page control."; }
        finally {_busy=false;}
    }

    private async Task SetFieldAsync(string value)
    {
        await RunAsync($"window.grevController?.setValue({JsonSerializer.Serialize(value)})");
        ShowBrowser();
    }
    private void ShowBrowser() { if(_browser is not null && IsEnabled) _browser.Visibility=Visibility.Visible;BrowseButton.Focus(); }
    private void Browse_Click(object sender,RoutedEventArgs e) { _browsing=true;_=RunAsync("window.grevController?.move(1)"); }
    private void Previous_Click(object sender,RoutedEventArgs e) { if(_browser?.CoreWebView2?.CanGoBack==true) _browser.CoreWebView2.GoBack(); }
    private void Home_Click(object sender,RoutedEventArgs e) => _browser?.CoreWebView2?.Navigate(_home.AbsoluteUri);
    private void Reload_Click(object sender,RoutedEventArgs e) => _browser?.CoreWebView2?.Reload();
    private void ScrollUp_Click(object sender,RoutedEventArgs e) => _=RunAsync("window.scrollBy(0,-500)");
    private void ScrollDown_Click(object sender,RoutedEventArgs e) => _=RunAsync("window.scrollBy(0,500)");
    private void Exit_Click(object sender,RoutedEventArgs e) => ExitRequested?.Invoke(this,EventArgs.Empty);
    public void Dispose() => DisposeBrowser();
    private void DisposeBrowser()
    {
        _generation++;_browsing=false;
        KeyboardOverlay.Cancel();ChoicesOverlay.Visibility=Visibility.Collapsed;
        _browser?.Dispose();_browser=null;BrowserHost.Children.Clear();
    }

    // No password is read back from the document. Typed text goes only to the
    // user-selected field, and submission still requires a separate A press.
    private const string ControllerScript="""
    (()=>{
      let selected=null, editing=null;
      const visible=e=>!e.disabled && e.getClientRects().length && getComputedStyle(e).visibility!=='hidden' && !e.closest('[inert]');
      const items=()=>Array.from(document.querySelectorAll('a[href],button,input:not([type=hidden]),textarea,select,[role=button],[tabindex="0"]')).filter(visible);
      const style=document.createElement('style');style.textContent='[data-grev-focus]{outline:4px solid #87b5ff!important;outline-offset:4px!important;box-shadow:0 0 20px #427cec!important}';document.head.append(style);
      window.grevController={
        move(d){let a=items();if(!a.length)return;let i=a.indexOf(selected);if(selected)selected.removeAttribute('data-grev-focus');
          selected=a[(i+d+a.length)%a.length];selected.setAttribute('data-grev-focus','');selected.focus({preventScroll:true});selected.scrollIntoView({block:'center',inline:'nearest',behavior:'smooth'});},
        activate(){if(!selected||!selected.isConnected)return null;
          if(selected.matches('select')){editing=selected;return {kind:'select',options:Array.from(selected.options).map(o=>({text:o.text,value:o.value,disabled:o.disabled}))};}
          if(selected.matches('textarea,input:not([type=button]):not([type=submit]):not([type=checkbox]):not([type=radio]):not([type=file]):not([type=range]):not([type=color])')){
            editing=selected;return {kind:'text',password:selected.type==='password'};}
          if(selected.matches('input[type=file],input[type=color],input[type=range]'))return null;
          selected.click();return null;},
        setValue(value){if(!editing||!editing.isConnected)return;editing.value=value;
          editing.dispatchEvent(new Event('input',{bubbles:true}));editing.dispatchEvent(new Event('change',{bubbles:true}));editing=null;}
      };
    })();
    """;
}
