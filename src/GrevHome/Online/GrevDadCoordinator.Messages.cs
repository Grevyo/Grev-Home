using System.Windows.Threading;
using GrevHome.Navigation;
using GrevHome.Views;

namespace GrevHome.Online;

public sealed partial class GrevDadCoordinator
{
    private readonly FriendMessagesView _messagesView = new();
    private readonly DispatcherTimer _messageTimer = new() { Interval=TimeSpan.FromSeconds(15) };
    private GrevDadFriend? _selectedMessageFriend;
    private int _messageGeneration;
    private bool _messageLoading, _messageSending;
    private string? _messageBefore, _retryBody, _retryId;

    private void InitializeMessages()
    {
        _friendProfileView.MessageRequested += (_,_)=>
        {
            if(_selectedMessageFriend is null || _selectedMessageFriend.UserId == _session.PrimaryUser?.GrevId) return;
            _oldestMessage=null;
            _messageGeneration++; _messageBefore=null; _retryBody=null; _retryId=null;
            _messagesView.Reset(_selectedMessageFriend.DisplayName);
            _navigation.Navigate(Route.FriendMessages);
        };
        _messagesView.BackRequested += (_,_)=>_navigation.GoBack();
        _messagesView.RefreshRequested += (_,_)=> { _messageBefore=null; _=LoadMessagesAsync(); };
        _messagesView.OlderRequested += (_,_)=>_=LoadMessagesAsync(older:true);
        _messagesView.SendRequested += (_,_)=>_=SendFriendMessageAsync();
        _messageTimer.Tick += (_,_)=> { if(_messageBefore is null) _=LoadMessagesAsync(); };
        _navigation.RouteChanged += route=>
        {
            _messageGeneration++;
            _messageTimer.Stop();
            if(route!=Route.FriendMessages) return;
            _routeHost.Content=_messagesView;
            _messageTimer.Start();
            _=LoadMessagesAsync();
        };
        _session.Changed += (_,_)=>_dispatcher.BeginInvoke(new Action(()=>
        {
            _messageGeneration++; _messageTimer.Stop(); _selectedMessageFriend=null;
            _retryId=null; _retryBody=null; _messagesView.Reset("Choose a friend");
            if(_navigation.Current==Route.FriendMessages) _navigation.GoBack();
        }));
    }
    private string? _oldestMessage;
    private async Task LoadMessagesAsync(bool older=false)
    {
        var owner=_session.PrimaryUser?.GrevId;
        var friend=_selectedMessageFriend;
        var generation=_messageGeneration;
        if(owner is null || friend is null || _messageLoading || _navigation.Current!=Route.FriendMessages) return;
        if(older) _messageBefore=_oldestMessage;
        _messageLoading=true;
        try
        {
            var service=RequireGrevDadAccountService();
            var page=await service.GetMessagesAsync(owner,friend.UserId,_messageBefore);
            if(generation!=_messageGeneration || owner!=_session.PrimaryUser?.GrevId) return;
            var messages=page.Messages??Array.Empty<GrevDadMessage>();
            _oldestMessage=messages.FirstOrDefault()?.Id;
            _messagesView.ShowMessages(messages,service.GetLastSnapshot(owner).Account?.UserId??"",page.HasMore);
            if(_messageBefore is null && messages.LastOrDefault() is {} last)
                await service.MarkMessagesReadAsync(owner,friend.UserId,last.Id);
        }
        catch(Exception ex) when(ex is HttpRequestException or IOException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        {
            if(generation==_messageGeneration) _messagesView.ShowStatus("Could not load messages. Check your connection or friendship and choose Refresh.");
        }
        finally { _messageLoading=false; }
    }
    private async Task SendFriendMessageAsync()
    {
        var owner=_session.PrimaryUser?.GrevId;
        var friend=_selectedMessageFriend;
        var generation=_messageGeneration;
        var body=_messagesView.Draft.Trim();
        if(owner is null || friend is null || _messageSending || body.Length==0) return;
        if(_retryBody!=body || _retryId is null) { _retryBody=body; _retryId=Guid.NewGuid().ToString(); }
        var id=_retryId;
        _messageSending=true; _messagesView.SetSending(true);
        try
        {
            await RequireGrevDadAccountService().SendMessageAsync(owner,friend.UserId,id,body);
            if(generation!=_messageGeneration || owner!=_session.PrimaryUser?.GrevId) return;
            _messagesView.ClearSentDraft(body); _retryId=null; _retryBody=null; _messageBefore=null;
            await LoadMessagesAsync();
        }
        catch(Exception ex) when(ex is HttpRequestException or IOException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        {
            if(generation==_messageGeneration) _messagesView.ShowStatus("Delivery not confirmed. Your draft is kept; Send / retry reuses the same message ID.");
        }
        finally { _messageSending=false; _messagesView.SetSending(false); }
    }
}
