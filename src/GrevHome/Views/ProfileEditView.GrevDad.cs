using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Online;
using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class ProfileEditView
{
    private readonly Border _grevDadEditorCard = new();
    private readonly TextBlock _grevDadConnectionText = new();
    private readonly TextBlock _grevDadIdentityText = new();
    private readonly TextBlock _grevDadCodeText = new();
    private readonly TextBlock _grevDadApprovalText = new();
    private readonly TextBlock _grevDadStatusText = new();
    private readonly Button _grevDadLinkButton = new();
    private readonly Button _grevDadOpenApprovalButton = new();
    private readonly Button _grevDadCopyCodeButton = new();
    private readonly Button _grevDadWebsiteButton = new();
    private readonly Button _grevDadCheckButton = new();
    private readonly Button _grevDadCancelButton = new();
    private readonly Button _grevDadUnlinkButton = new();
    private readonly Button _grevDadSharePresenceButton = new();
    private readonly Button _grevDadSharePlayingButton = new();
    private readonly Button _grevDadShareActivityButton = new();
    private readonly Button _grevDadShareHistoryButton = new();
    private readonly Button _grevDadActivityVisibilityButton = new();
    private readonly Button _grevDadHistoryVisibilityButton = new();
    private LocalProfile? _grevDadProfile;
    private bool _canManageGrevDad;
    private GrevDadLinkStart? _grevDadLinkStart;
    private GrevDadPrivacySettings _grevDadPrivacy = GrevDadPrivacySettings.Default;
    private bool _grevDadEditorBuilt;

    public event EventHandler? LinkGrevDadRequested;
    public event EventHandler? CheckGrevDadLinkRequested;
    public event EventHandler? CancelGrevDadLinkRequested;
    public event EventHandler? UnlinkGrevDadRequested;
    public event Action<Uri>? OpenGrevDadApprovalRequested;
    public event EventHandler? OpenGrevDadWebsiteRequested;
    public event Action<GrevDadPrivacySettings>? SaveGrevDadPrivacyRequested;

    public void InitializeGrevDadEditor()
    {
        if (_grevDadEditorBuilt || ProfileEditCard.Child is not StackPanel content)
        {
            return;
        }

        _grevDadEditorBuilt = true;
        _grevDadEditorCard.Margin = new Thickness(0, 22, 0, 0);
        _grevDadEditorCard.Padding = new Thickness(20);
        _grevDadEditorCard.Background = new SolidColorBrush(Color.FromRgb(9, 12, 18));
        _grevDadEditorCard.BorderBrush = new SolidColorBrush(Color.FromRgb(43, 51, 68));
        _grevDadEditorCard.BorderThickness = new Thickness(1);
        _grevDadEditorCard.CornerRadius = new CornerRadius(0);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "GREV.DAD ACCOUNT",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Add or remove the Grev.dad account linked to this GrevID and choose what this profile shares online. Your Grev.dad password is never stored in Grev Home.",
            Margin = new Thickness(0, 7, 0, 0),
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        _grevDadConnectionText.Margin = new Thickness(0, 14, 0, 0);
        _grevDadConnectionText.FontSize = 20;
        _grevDadConnectionText.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(_grevDadConnectionText);

        _grevDadIdentityText.Margin = new Thickness(0, 5, 0, 0);
        _grevDadIdentityText.Foreground = (Brush)FindResource("MutedBrush");
        _grevDadIdentityText.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(_grevDadIdentityText);

        _grevDadCodeText.Margin = new Thickness(0, 12, 0, 0);
        _grevDadCodeText.FontSize = 22;
        _grevDadCodeText.FontWeight = FontWeights.Bold;
        _grevDadCodeText.Foreground = (Brush)FindResource("AccentBrush");
        _grevDadCodeText.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(_grevDadCodeText);

        _grevDadApprovalText.Margin = new Thickness(0, 4, 0, 0);
        _grevDadApprovalText.Foreground = (Brush)FindResource("MutedBrush");
        _grevDadApprovalText.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(_grevDadApprovalText);

        stack.Children.Add(new TextBlock
        {
            Text = "First open Grev.dad and sign in to the account you want to link. Return here to generate a code, approve it on the website, then choose Check approval.",
            Margin = new Thickness(0, 14, 0, 10),
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        ConfigureGrevDadButton(_grevDadWebsiteButton,"Open Grev.dad • sign in",230,(_,_)=>OpenGrevDadWebsiteRequested?.Invoke(this,EventArgs.Empty));
        _grevDadWebsiteButton.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(_grevDadWebsiteButton);
        var accountActions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        ConfigureGrevDadButton(_grevDadLinkButton, "Generate approval code", 230, (_, _) => LinkGrevDadRequested?.Invoke(this, EventArgs.Empty));
        ConfigureGrevDadButton(_grevDadOpenApprovalButton, "Open approval page", 200, (_, _) =>
        {
            if (_grevDadLinkStart is { } link)
            {
                OpenGrevDadApprovalRequested?.Invoke(link.VerificationUri);
            }
        });
        ConfigureGrevDadButton(_grevDadCheckButton, "Check approval", 165, (_, _) => CheckGrevDadLinkRequested?.Invoke(this, EventArgs.Empty));
        ConfigureGrevDadButton(_grevDadCopyCodeButton,"Copy code",150,(_,_)=>
        {
            if(_grevDadLinkStart is not { } link || link.ExpiresAtUtc<=DateTimeOffset.UtcNow)
            { ShowGrevDadStatus("That code has expired. Start a new link request.");return; }
            try { Clipboard.SetText(link.UserCode);ShowGrevDadStatus("Approval code copied. Choose Open approval page to continue."); }
            catch(System.Runtime.InteropServices.ExternalException) {ShowGrevDadStatus("The clipboard is busy. Try Copy code again.");}
        });
        ConfigureGrevDadButton(_grevDadCancelButton, "Cancel link", 150, (_, _) => CancelGrevDadLinkRequested?.Invoke(this, EventArgs.Empty));
        ConfigureGrevDadButton(_grevDadUnlinkButton, "Unlink Grev.dad", 170, (_, _) => UnlinkGrevDadRequested?.Invoke(this, EventArgs.Empty));
        accountActions.Children.Add(_grevDadLinkButton);
        accountActions.Children.Add(_grevDadOpenApprovalButton);
        accountActions.Children.Add(_grevDadCopyCodeButton);
        accountActions.Children.Add(_grevDadCheckButton);
        accountActions.Children.Add(_grevDadCancelButton);
        accountActions.Children.Add(_grevDadUnlinkButton);
        stack.Children.Add(accountActions);

        stack.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 14, 0, 14),
            Background = new SolidColorBrush(Color.FromRgb(43, 51, 68))
        });
        stack.Children.Add(new TextBlock
        {
            Text = "PRIVACY & ACTIVITY SHARING",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("MutedBrush")
        });

        var sharing = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        ConfigureGrevDadButton(_grevDadSharePresenceButton, "Presence", 170, (_, _) => ToggleGrevDadPrivacy(value => value with { SharePresence = !value.SharePresence }));
        ConfigureGrevDadButton(_grevDadSharePlayingButton, "Playing status", 185, (_, _) => ToggleGrevDadPrivacy(value => value with { SharePlayingStatus = !value.SharePlayingStatus }));
        ConfigureGrevDadButton(_grevDadShareActivityButton, "Live activity", 180, (_, _) => ToggleGrevDadPrivacy(value => value with { ShareLiveActivityEvents = !value.ShareLiveActivityEvents }));
        ConfigureGrevDadButton(_grevDadShareHistoryButton, "Session history", 190, (_, _) => ToggleGrevDadPrivacy(value => value with { ShareSessionHistory = !value.ShareSessionHistory }));
        sharing.Children.Add(_grevDadSharePresenceButton);
        sharing.Children.Add(_grevDadSharePlayingButton);
        sharing.Children.Add(_grevDadShareActivityButton);
        sharing.Children.Add(_grevDadShareHistoryButton);
        stack.Children.Add(sharing);

        var visibility = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        ConfigureGrevDadButton(_grevDadActivityVisibilityButton, "Activity visibility", 220, (_, _) => ToggleGrevDadPrivacy(value => value with
        {
            ActivityVisibility = string.Equals(value.ActivityVisibility, "friends", StringComparison.OrdinalIgnoreCase) ? "private" : "friends"
        }));
        ConfigureGrevDadButton(_grevDadHistoryVisibilityButton, "History visibility", 220, (_, _) => ToggleGrevDadPrivacy(value => value with
        {
            HistoryVisibility = string.Equals(value.HistoryVisibility, "friends", StringComparison.OrdinalIgnoreCase) ? "private" : "friends"
        }));
        visibility.Children.Add(_grevDadActivityVisibilityButton);
        visibility.Children.Add(_grevDadHistoryVisibilityButton);
        stack.Children.Add(visibility);

        _grevDadStatusText.Margin = new Thickness(0, 8, 0, 0);
        _grevDadStatusText.Foreground = (Brush)FindResource("MutedBrush");
        _grevDadStatusText.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(_grevDadStatusText);

        _grevDadEditorCard.Child = stack;

        var insertIndex = Math.Max(0, content.Children.Count - 2);
        content.Children.Insert(insertIndex, _grevDadEditorCard);
        RenderGrevDadPrivacy();
    }

    public void SetGrevDadContext(LocalProfile? profile, bool canManage)
    {
        InitializeGrevDadEditor();
        _grevDadProfile = profile;
        _canManageGrevDad = profile is not null && canManage;
        _grevDadEditorCard.Visibility = profile is null || profile.IsBuiltInGuest ? Visibility.Collapsed : Visibility.Visible;
        RenderGrevDadPrivacy();

        if (profile is null)
        {
            return;
        }

        _grevDadStatusText.Text = _canManageGrevDad
            ? "This GrevID is the current Primary User, so its Grev.dad link and sharing can be changed here."
            : "Only this GrevID as the current Primary User can change its Grev.dad account link and sharing settings.";
    }

    public void SetGrevDadState(GrevDadAccountSnapshot snapshot, GrevDadLinkStart? activeLink = null)
    {
        InitializeGrevDadEditor();
        _grevDadLinkStart = activeLink ?? _grevDadLinkStart;
        _grevDadConnectionText.Text = snapshot.State switch
        {
            GrevDadConnectionState.Linked => "Linked",
            GrevDadConnectionState.Linking => "Waiting for website approval",
            GrevDadConnectionState.Offline => "Linked • Grev.dad offline",
            GrevDadConnectionState.Expired => "Link expired",
            GrevDadConnectionState.Revoked => "Link revoked",
            GrevDadConnectionState.Error => "Link error",
            _ => "Not linked"
        };
        _grevDadIdentityText.Text = snapshot.Account is { } account
            ? $"@{account.Username} • {account.DisplayName}"
            : snapshot.Message ?? "No Grev.dad account is linked to this GrevID.";

        if (snapshot.State == GrevDadConnectionState.Linking && _grevDadLinkStart is { } link)
        {
            _grevDadCodeText.Text = $"Approval code: {link.UserCode}";
            _grevDadApprovalText.Text = $"Paste this code on your Grev.dad profile, or choose Open approval page to fill it automatically. After approving on the website, choose Check approval here. Expires {link.ExpiresAtUtc.ToLocalTime():t}.";
        }
        else
        {
            _grevDadCodeText.Text = string.Empty;
            _grevDadApprovalText.Text = string.Empty;
            if (snapshot.State != GrevDadConnectionState.Linking)
            {
                _grevDadLinkStart = null;
            }
        }

        SetGrevDadAccountButtons(snapshot.State);
    }

    public void SetGrevDadPrivacyState(GrevDadPrivacySettings settings, string? status = null)
    {
        _grevDadPrivacy = settings;
        RenderGrevDadPrivacy();
        if (status is not null)
        {
            _grevDadStatusText.Text = status;
        }
    }

    public void ShowGrevDadStatus(string message) => _grevDadStatusText.Text = message;

    private void SetGrevDadAccountButtons(GrevDadConnectionState state)
    {
        var linking = state == GrevDadConnectionState.Linking;
        var linked = state is GrevDadConnectionState.Linked or GrevDadConnectionState.Offline;
        var relinkable = state is GrevDadConnectionState.Unlinked or GrevDadConnectionState.Expired or GrevDadConnectionState.Revoked or GrevDadConnectionState.Error;

        SetButtonState(_grevDadLinkButton, _canManageGrevDad && relinkable, relinkable);
        SetButtonState(_grevDadOpenApprovalButton, _canManageGrevDad && linking && _grevDadLinkStart is not null, linking && _grevDadLinkStart is not null);
        SetButtonState(_grevDadCopyCodeButton,_canManageGrevDad && linking && _grevDadLinkStart is not null,linking && _grevDadLinkStart is not null);
        SetButtonState(_grevDadWebsiteButton,_canManageGrevDad,true);
        SetButtonState(_grevDadCheckButton, _canManageGrevDad && linking, linking);
        if (linking)
        {
            _grevDadCheckButton.Background = new SolidColorBrush(Color.FromRgb(24, 105, 57));
            _grevDadCheckButton.Foreground = Brushes.White;
            _grevDadOpenApprovalButton.Background = new SolidColorBrush(Color.FromRgb(24, 105, 57));
            _grevDadOpenApprovalButton.Foreground = Brushes.White;
        }
        else
        {
            _grevDadCheckButton.ClearValue(Button.BackgroundProperty);
            _grevDadCheckButton.ClearValue(Button.ForegroundProperty);
            _grevDadOpenApprovalButton.ClearValue(Button.BackgroundProperty);
            _grevDadOpenApprovalButton.ClearValue(Button.ForegroundProperty);
        }
        SetButtonState(_grevDadCancelButton, _canManageGrevDad && linking, linking);
        SetButtonState(_grevDadUnlinkButton, _canManageGrevDad && (linked || state is GrevDadConnectionState.Expired or GrevDadConnectionState.Revoked or GrevDadConnectionState.Error), linked || state is GrevDadConnectionState.Expired or GrevDadConnectionState.Revoked or GrevDadConnectionState.Error);
    }

    private static void SetButtonState(Button button, bool enabled, bool visible)
    {
        button.IsEnabled = enabled;
        button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConfigureGrevDadButton(Button button, string content, double minWidth, RoutedEventHandler handler)
    {
        button.Content = content;
        button.MinWidth = minWidth;
        button.MinHeight = 46;
        button.Margin = new Thickness(0, 0, 10, 10);
        button.Click += handler;
    }

    private void ToggleGrevDadPrivacy(Func<GrevDadPrivacySettings, GrevDadPrivacySettings> mutate)
    {
        if (!_canManageGrevDad)
        {
            return;
        }

        _grevDadPrivacy = mutate(_grevDadPrivacy);
        RenderGrevDadPrivacy();
        _grevDadStatusText.Text = "Saving…";
        SaveGrevDadPrivacyRequested?.Invoke(_grevDadPrivacy);
    }

    private void RenderGrevDadPrivacy()
    {
        foreach (var button in new[]
                 {
                     _grevDadSharePresenceButton,
                     _grevDadSharePlayingButton,
                     _grevDadShareActivityButton,
                     _grevDadShareHistoryButton,
                     _grevDadActivityVisibilityButton,
                     _grevDadHistoryVisibilityButton
                 })
        {
            button.IsEnabled = _canManageGrevDad;
        }

        _grevDadSharePresenceButton.Content = $"Presence: {OnOff(_grevDadPrivacy.SharePresence)}";
        _grevDadSharePlayingButton.Content = $"Playing status: {OnOff(_grevDadPrivacy.SharePlayingStatus)}";
        _grevDadShareActivityButton.Content = $"Live activity: {OnOff(_grevDadPrivacy.ShareLiveActivityEvents)}";
        _grevDadShareHistoryButton.Content = $"Session history: {OnOff(_grevDadPrivacy.ShareSessionHistory)}";
        _grevDadActivityVisibilityButton.Content = $"Activity visibility: {FormatVisibility(_grevDadPrivacy.ActivityVisibility)}";
        _grevDadHistoryVisibilityButton.Content = $"History visibility: {FormatVisibility(_grevDadPrivacy.HistoryVisibility)}";
    }

    private static string OnOff(bool value) => value ? "On" : "Off";
    private static string FormatVisibility(string value) =>
        string.Equals(value, "friends", StringComparison.OrdinalIgnoreCase) ? "Friends" : "Private";
}
