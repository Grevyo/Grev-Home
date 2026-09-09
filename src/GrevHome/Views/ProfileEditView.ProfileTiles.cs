using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class ProfileEditView
{
    private readonly Border _profileTilesEditorCard = new();
    private readonly Button _profileTilesEditorButton = new();
    private bool _profileTilesEditorBuilt;

    public event EventHandler? ProfileTilesRequested;

    public void InitializeProfileTilesEditorLink()
    {
        if (_profileTilesEditorBuilt || ProfileEditCard.Child is not StackPanel content) return;
        _profileTilesEditorBuilt = true;

        _profileTilesEditorCard.Margin = new Thickness(0, 14, 0, 0);
        _profileTilesEditorCard.Padding = new Thickness(20);
        _profileTilesEditorCard.Background = new SolidColorBrush(Color.FromRgb(13, 17, 25));
        _profileTilesEditorCard.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 45, 59));
        _profileTilesEditorCard.BorderThickness = new Thickness(1);
        _profileTilesEditorCard.CornerRadius = new CornerRadius(0);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "PROFILE LAYOUT",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Edit the same 8-column profile tile layout used on Grev.dad. Linked profiles sync this layout both ways, including after a fresh Grev Home install.",
            Margin = new Thickness(0, 6, 0, 12),
            Foreground = (Brush)FindResource("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        _profileTilesEditorButton.Content = "Edit Profile Tiles";
        _profileTilesEditorButton.Width = 210;
        _profileTilesEditorButton.Height = 48;
        _profileTilesEditorButton.HorizontalAlignment = HorizontalAlignment.Left;
        _profileTilesEditorButton.Click += (_, _) => ProfileTilesRequested?.Invoke(this, EventArgs.Empty);
        stack.Children.Add(_profileTilesEditorButton);

        _profileTilesEditorCard.Child = stack;
        var insertIndex = Math.Max(0, content.Children.Count - 2);
        content.Children.Insert(insertIndex, _profileTilesEditorCard);
    }

    public void SetProfileTilesEditorContext(LocalProfile? profile, bool canEdit)
    {
        InitializeProfileTilesEditorLink();
        _profileTilesEditorCard.Visibility = profile is null || profile.IsBuiltInGuest
            ? Visibility.Collapsed
            : Visibility.Visible;
        _profileTilesEditorButton.IsEnabled = profile is not null && canEdit && !profile.IsBuiltInGuest;
    }
}
