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
    private bool _grevDadUiBuilt;
    private GrevDadLinkStart? _grevDadLinkStart;

    public event EventHandler? LinkGrevDadRequested;
    public event EventHandler? CheckGrevDadLinkRequested;
    public event EventHandler? CancelGrevDadLinkRequested;
    public event EventHandler? UnlinkGrevDadRequested;
    public event Action<Uri>? OpenGrevDadApprovalRequested;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Dispatcher.BeginInvoke(new Action(BuildGrevDadAccountUi));
    }

    private void BuildGrevDadAccountUi()
    {
        if (_grevDadUiBuilt || AccountSectionContent.Child is not StackPanel accountPanel)
        {
            return;
        }

        _grevDadUiBuilt = true;
        var card = new Border
        {
            Margin = new Thickness(0, 22, 0, 0),
            Padding = new Thickness(18),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(11, 14, 21)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 61, 81)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "GREV.DAD ACCOUNT",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush")
        });
        content.Children.Add(new TextBlock
        {
            Text = "Link this permanent local GrevID to one Grev.dad account. Your Grev.dad password never enters Grev Home; approval happens on the website and Windows stores only the resulting device credential.",
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

        card.Child = content;
        accountPanel.Children.Add(card);
        SetGrevDadState(_profile, GrevDadAccountSnapshot.Unlinked);
    }

    private void ConfigureGrevDadButton(Button button, string content, double minWidth, RoutedEventHandler handler)
    {
        button.Content = content;
        button.MinWidth = minWidth;
        button.Margin = new Thickness(0, 0, 10, 10);
        button.Style = (Style)FindResource("WrappedSettingsButtonStyle");
        button.Click += handler;
    }

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
