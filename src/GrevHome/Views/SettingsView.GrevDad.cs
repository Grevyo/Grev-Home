using System.Windows;
using System.Windows.Controls;
using GrevHome.Online;
using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class SettingsView
{
    private readonly TextBlock _grevDadConnectionText = new();
    private readonly TextBlock _grevDadIdentityText = new();
    private readonly TextBlock _grevDadCodeText = new();
    private readonly TextBlock _grevDadApprovalText = new();
    private readonly Button _grevDadLinkButton = new();
    private readonly Button _grevDadOpenApprovalButton = new();
    private readonly Button _grevDadCheckButton = new();
    private readonly Button _grevDadCancelButton = new();
    private readonly Button _grevDadUnlinkButton = new();
    private readonly Button _grevDadSharePresenceButton = new();
    private readonly Button _grevDadSharePlayingButton = new();
    private readonly Button _grevDadShareActivityButton = new();
    private readonly Button _grevDadShareHistoryButton = new();
    private readonly Button _grevDadActivityVisibilityButton = new();
    private readonly Button _grevDadHistoryVisibilityButton = new();
    private readonly TextBlock _grevDadPrivacyStatusText = new();
    private Button? _grevDadSectionButton;
    private Border? _grevDadSectionContent;
    private bool _grevDadUiBuilt;
    private GrevDadLinkStart? _grevDadLinkStart;
    private GrevDadPrivacySettings _grevDadPrivacySettings = GrevDadPrivacySettings.Default;

    public event EventHandler? LinkGrevDadRequested;
    public event EventHandler? CheckGrevDadLinkRequested;
    public event EventHandler? CancelGrevDadLinkRequested;
    public event EventHandler? UnlinkGrevDadRequested;
    public event Action<Uri>? OpenGrevDadApprovalRequested;
    public event Action<GrevDadPrivacySettings>? SaveGrevDadPrivacyRequested;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Dispatcher.BeginInvoke(new Action(BuildGrevDadAccountUi));
    }

    private void BuildGrevDadAccountUi()
    {
        if (_grevDadUiBuilt ||
            AccountSectionButton.Parent is not StackPanel accountSection ||
            accountSection.Parent is not StackPanel rootPanel)
        {
            return;
        }

        _grevDadUiBuilt = true;

        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        _grevDadSectionButton = new Button
        {
            Content = "GREV.DAD  ▾",
            Style = (Style)FindResource("SettingsSectionHeaderButtonStyle")
        };
        _grevDadSectionButton.Click += (_, _) => ToggleGrevDadSection();
        section.Children.Add(_grevDadSectionButton);

        _grevDadSectionContent = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 21, 30)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 51, 68)),
            BorderThickness = new Thickness(1, 0, 1, 1),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(0, 0, 14, 14)
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "GREV.DAD ACCOUNT",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush")
        });
        content.Children.Add(new TextBlock
        {
            Text = "Link this permanent local GrevID to one Grev.dad account. Grev.dad is optional: local login, apps, saves, history, playtime and Grev Home XP continue without it.",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        _grevDadConnectionText.Margin = new Thickness(0, 14, 0, 0);
        _grevDadConnectionText.FontSize = 18;
        _grevDadConnectionText.FontWeight = FontWeights.SemiBold;
        _grevDadConnectionText.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(_grevDadConnectionText);

        _grevDadIdentityText.Margin = new Thickness(0, 5, 0, 0);
        _grevDadIdentityText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        _grevDadIdentityText.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(_grevDadIdentityText);

        _grevDadCodeText.Margin = new Thickness(0, 14, 0, 0);
        _grevDadCodeText.FontSize = 24;
        _grevDadCodeText.FontWeight = FontWeights.Bold;
        _grevDadCodeText.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
        _grevDadCodeText.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(_grevDadCodeText);

        _grevDadApprovalText.Margin = new Thickness(0, 5, 0, 0);
        _grevDadApprovalText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        _grevDadApprovalText.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(_grevDadApprovalText);

        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        ConfigureGrevDadButton(_grevDadLinkButton, "Link Grev.dad", 170, (_, _) => LinkGrevDadRequested?.Invoke(this, EventArgs.Empty));
        ConfigureGrevDadButton(_grevDadOpenApprovalButton, "Open approval page", 200, (_, _) =>
        {
            if (_grevDadLinkStart is { } link)
            {
                OpenGrevDadApprovalRequested?.Invoke(link.VerificationUri);
            }
        });
        ConfigureGrevDadButton(_grevDadCheckButton, "Check approval", 170, (_, _) => CheckGrevDadLinkRequested?.Invoke(this, EventArgs.Empty));
        ConfigureGrevDadButton(_grevDadCancelButton, "Cancel link", 150, (_, _) => CancelGrevDadLinkRequested?.Invoke(this, EventArgs.Empty));
        ConfigureGrevDadButton(_grevDadUnlinkButton, "Unlink Grev.dad", 170, (_, _) => UnlinkGrevDadRequested?.Invoke(this, EventArgs.Empty));
        actions.Children.Add(_grevDadLinkButton);
        actions.Children.Add(_grevDadOpenApprovalButton);
        actions.Children.Add(_grevDadCheckButton);
        actions.Children.Add(_grevDadCancelButton);
        actions.Children.Add(_grevDadUnlinkButton);
        content.Children.Add(actions);

        content.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 18, 0, 18),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 51, 68))
        });
        content.Children.Add(new TextBlock
        {
            Text = "PRIVACY & ACTIVITY SHARING",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush")
        });
        content.Children.Add(new TextBlock
        {
            Text = "These switches control only what Grev Home publishes to Grev.dad. They never disable local session history, playtime, XP or profile data.",
            Margin = new Thickness(0, 8, 0, 12),
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        var privacyButtons = new WrapPanel();
        ConfigureGrevDadButton(_grevDadSharePresenceButton, "Presence", 170, (_, _) => TogglePrivacy(settings => settings with { SharePresence = !settings.SharePresence }));
        ConfigureGrevDadButton(_grevDadSharePlayingButton, "Playing status", 190, (_, _) => TogglePrivacy(settings => settings with { SharePlayingStatus = !settings.SharePlayingStatus }));
        ConfigureGrevDadButton(_grevDadShareActivityButton, "Live activity", 180, (_, _) => TogglePrivacy(settings => settings with { ShareLiveActivityEvents = !settings.ShareLiveActivityEvents }));
        ConfigureGrevDadButton(_grevDadShareHistoryButton, "Session history", 190, (_, _) => TogglePrivacy(settings => settings with { ShareSessionHistory = !settings.ShareSessionHistory }));
        privacyButtons.Children.Add(_grevDadSharePresenceButton);
        privacyButtons.Children.Add(_grevDadSharePlayingButton);
        privacyButtons.Children.Add(_grevDadShareActivityButton);
        privacyButtons.Children.Add(_grevDadShareHistoryButton);
        content.Children.Add(privacyButtons);

        var visibilityButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        ConfigureGrevDadButton(_grevDadActivityVisibilityButton, "Activity visibility", 230, (_, _) => TogglePrivacy(settings => settings with
        {
            ActivityVisibility = string.Equals(settings.ActivityVisibility, "friends", StringComparison.OrdinalIgnoreCase) ? "private" : "friends"
        }));
        ConfigureGrevDadButton(_grevDadHistoryVisibilityButton, "History visibility", 230, (_, _) => TogglePrivacy(settings => settings with
        {
            HistoryVisibility = string.Equals(settings.HistoryVisibility, "friends", StringComparison.OrdinalIgnoreCase) ? "private" : "friends"
        }));
        visibilityButtons.Children.Add(_grevDadActivityVisibilityButton);
        visibilityButtons.Children.Add(_grevDadHistoryVisibilityButton);
        content.Children.Add(visibilityButtons);

        _grevDadPrivacyStatusText.Margin = new Thickness(0, 8, 0, 0);
        _grevDadPrivacyStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
        _grevDadPrivacyStatusText.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(_grevDadPrivacyStatusText);

        _grevDadSectionContent.Child = content;
        section.Children.Add(_grevDadSectionContent);
        var accountIndex = rootPanel.Children.IndexOf(accountSection);
        rootPanel.Children.Insert(Math.Max(0, accountIndex + 1), section);

        SetGrevDadState(_profile, GrevDadAccountSnapshot.Unlinked);
        RenderGrevDadPrivacy();
    }

    private void ToggleGrevDadSection()
    {
        if (_grevDadSectionButton is null || _grevDadSectionContent is null)
        {
            return;
        }

        var expand = _grevDadSectionContent.Visibility != Visibility.Visible;
        _grevDadSectionContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        _grevDadSectionButton.Content = $"GREV.DAD  {(expand ? "▴" : "▾")}";
        _grevDadSectionButton.Focus();
    }

    public void OpenGrevDadSection()
    {
        BuildGrevDadAccountUi();
        if (_grevDadSectionButton is null || _grevDadSectionContent is null)
        {
            return;
        }

        _grevDadSectionContent.Visibility = Visibility.Visible;
        _grevDadSectionButton.Content = "GREV.DAD  ▴";
        _grevDadSectionButton.Focus();
    }

    private void ConfigureGrevDadButton(Button button, string content, double minWidth, RoutedEventHandler handler)
    {
        button.Content = content;
        button.MinWidth = minWidth;
        button.Margin = new Thickness(0, 0, 10, 10);
        button.Style = (Style)FindResource("WrappedSettingsButtonStyle");
        button.Click += handler;
    }

    private void TogglePrivacy(Func<GrevDadPrivacySettings, GrevDadPrivacySettings> mutate)
    {
        if (_profile is null)
        {
            _grevDadPrivacyStatusText.Text = "A persistent local Primary User is required to change Grev.dad privacy settings.";
            return;
        }

        _grevDadPrivacySettings = mutate(_grevDadPrivacySettings);
        RenderGrevDadPrivacy();
        _grevDadPrivacyStatusText.Text = "Saving…";
        SaveGrevDadPrivacyRequested?.Invoke(_grevDadPrivacySettings);
    }

    public void SetGrevDadPrivacyState(GrevDadPrivacySettings settings, string? status = null)
    {
        _grevDadPrivacySettings = settings;
        RenderGrevDadPrivacy();
        _grevDadPrivacyStatusText.Text = status ?? string.Empty;
    }

    public void ShowGrevDadPrivacyStatus(string message) => _grevDadPrivacyStatusText.Text = message;

    private void RenderGrevDadPrivacy()
    {
        var enabled = _profile is not null;
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
            button.IsEnabled = enabled;
        }

        _grevDadSharePresenceButton.Content = $"Presence: {OnOff(_grevDadPrivacySettings.SharePresence)}";
        _grevDadSharePlayingButton.Content = $"Playing status: {OnOff(_grevDadPrivacySettings.SharePlayingStatus)}";
        _grevDadShareActivityButton.Content = $"Live activity: {OnOff(_grevDadPrivacySettings.ShareLiveActivityEvents)}";
        _grevDadShareHistoryButton.Content = $"Session history: {OnOff(_grevDadPrivacySettings.ShareSessionHistory)}";
        _grevDadActivityVisibilityButton.Content = $"Activity visibility: {FormatVisibility(_grevDadPrivacySettings.ActivityVisibility)}";
        _grevDadHistoryVisibilityButton.Content = $"History visibility: {FormatVisibility(_grevDadPrivacySettings.HistoryVisibility)}";
    }

    private static string OnOff(bool value) => value ? "On" : "Off";
    private static string FormatVisibility(string value) =>
        string.Equals(value, "friends", StringComparison.OrdinalIgnoreCase) ? "Friends" : "Private";

    public void SetGrevDadState(
        LocalProfile? profile,
        GrevDadAccountSnapshot snapshot,
        GrevDadLinkStart? activeLink = null)
    {
        _grevDadLinkStart = activeLink ?? _grevDadLinkStart;

        if (!_grevDadUiBuilt)
        {
            return;
        }

        if (profile is null)
        {
            _grevDadConnectionText.Text = "Grev.dad linking requires a local Primary User.";
            _grevDadIdentityText.Text = "Temporary Guest sessions do not own online account credentials.";
            _grevDadCodeText.Text = string.Empty;
            _grevDadApprovalText.Text = string.Empty;
            SetGrevDadButtons(link: false, approval: false, check: false, cancel: false, unlink: false);
            RenderGrevDadPrivacy();
            return;
        }

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
            ? $"Grev.dad: @{account.Username} • {account.DisplayName} • remote user {account.UserId}"
            : snapshot.Message ?? "This local GrevID has no Grev.dad account linked yet.";

        if (snapshot.State == GrevDadConnectionState.Linking && _grevDadLinkStart is { } link)
        {
            _grevDadCodeText.Text = $"Approval code: {link.UserCode}";
            _grevDadApprovalText.Text = $"Approve at {link.VerificationUri} • expires {link.ExpiresAtUtc.ToLocalTime():t}";
        }
        else if (snapshot.State == GrevDadConnectionState.Linking)
        {
            _grevDadCodeText.Text = "A link request is still pending.";
            _grevDadApprovalText.Text = "If you no longer have its approval code, cancel this request and create a new one.";
        }
        else
        {
            _grevDadCodeText.Text = string.Empty;
            _grevDadApprovalText.Text = string.Empty;
            _grevDadLinkStart = null;
        }

        switch (snapshot.State)
        {
            case GrevDadConnectionState.Linking:
                SetGrevDadButtons(link: false, approval: _grevDadLinkStart is not null, check: true, cancel: true, unlink: false);
                break;
            case GrevDadConnectionState.Linked:
            case GrevDadConnectionState.Offline:
                SetGrevDadButtons(link: false, approval: false, check: false, cancel: false, unlink: true);
                break;
            case GrevDadConnectionState.Expired:
            case GrevDadConnectionState.Revoked:
            case GrevDadConnectionState.Error:
                SetGrevDadButtons(link: true, approval: false, check: false, cancel: false, unlink: true);
                break;
            default:
                SetGrevDadButtons(link: true, approval: false, check: false, cancel: false, unlink: false);
                break;
        }
        RenderGrevDadPrivacy();
    }

    private void SetGrevDadButtons(bool link, bool approval, bool check, bool cancel, bool unlink)
    {
        _grevDadLinkButton.IsEnabled = link;
        _grevDadOpenApprovalButton.IsEnabled = approval;
        _grevDadCheckButton.IsEnabled = check;
        _grevDadCancelButton.IsEnabled = cancel;
        _grevDadUnlinkButton.IsEnabled = unlink;
        _grevDadLinkButton.Visibility = link ? Visibility.Visible : Visibility.Collapsed;
        _grevDadOpenApprovalButton.Visibility = approval ? Visibility.Visible : Visibility.Collapsed;
        _grevDadCheckButton.Visibility = check ? Visibility.Visible : Visibility.Collapsed;
        _grevDadCancelButton.Visibility = cancel ? Visibility.Visible : Visibility.Collapsed;
        _grevDadUnlinkButton.Visibility = unlink ? Visibility.Visible : Visibility.Collapsed;
    }
}
