using System.Windows;
using System.Windows.Controls;
using GrevHome.Online;

namespace GrevHome.Views;

public sealed class FriendMessagesView : UserControl
{
    private readonly TextBlock _title = new() { FontSize = 32 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _messages = new();
    private readonly TextBox _draft = new() { IsReadOnly = true, IsTabStop = false, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
    private readonly ControllerQwertyKeyboard _keyboard = new();
    private readonly Button _send;
    private readonly Button _older;
    public string Draft => _draft.Text;
    public event EventHandler? BackRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? OlderRequested;
    public event EventHandler? SendRequested;

    public FriendMessagesView()
    {
        var root = new Grid();
        var layout = new DockPanel { Margin = new Thickness(54,26,54,30) };
        root.Children.Add(layout);
        DockPanel.SetDock(_title, Dock.Top);
        layout.Children.Add(_title);
        var footer = new StackPanel();
        DockPanel.SetDock(footer,Dock.Bottom);
        footer.Children.Add(_status);
        footer.Children.Add(_draft);
        var buttons = new WrapPanel();
        footer.Children.Add(buttons);
        Button Add(string text, Action action)
        {
            var button = new Button { Content=text, MinWidth=140, Height=54, Margin=new Thickness(0,10,10,0) };
            button.Click += (_,_) => action();
            buttons.Children.Add(button);
            return button;
        }
        Add("Write message",()=>_keyboard.Open("Message your friend",_draft.Text,2000));
        _send=Add("Send / retry",()=>SendRequested?.Invoke(this,EventArgs.Empty));
        Add("Refresh",()=>RefreshRequested?.Invoke(this,EventArgs.Empty));
        _older=Add("Older messages",()=>OlderRequested?.Invoke(this,EventArgs.Empty));
        Add("Back",()=>BackRequested?.Invoke(this,EventArgs.Empty));
        layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content=_messages, VerticalScrollBarVisibility=ScrollBarVisibility.Hidden });
        root.Children.Add(_keyboard);
        Panel.SetZIndex(_keyboard,100);
        _keyboard.Completed += value=>_draft.Text=value;
        Content=root;
    }
    public void Reset(string name)
    {
        _title.Text=$"Messages • {name}";
        _draft.Clear(); _messages.Children.Clear(); _older.IsEnabled=false;
        _status.Text="History is stored on Grev.dad. Loading…";
    }
    public void ShowMessages(IReadOnlyList<GrevDadMessage> messages, string ownId, bool hasMore)
    {
        _messages.Children.Clear();
        foreach(var message in messages)
        {
            _messages.Children.Add(new TextBlock {
                Text=$"{(message.SenderUserId==ownId ? "You" : "Friend")} • {DateTimeOffset.FromUnixTimeSeconds(message.CreatedAt).ToLocalTime():g}\n{(message.Type=="text" ? message.Body : "[Media message — view on Grev.dad]")}",
                FontSize=18, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(8,12,8,12)
            });
        }
        _older.IsEnabled=hasMore;
        _status.Text=messages.Count==0 ? "No messages yet." : "History loaded from Grev.dad.";
    }
    public void ShowStatus(string message)=>_status.Text=message;
    public void SetSending(bool busy)=>_send.IsEnabled=!busy;
    public void ClearSentDraft(string sent) { if (_draft.Text==sent) _draft.Clear(); }
}
