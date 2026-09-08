using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class ProfileEditView
{
    private bool _presentationPreviewHooksAttached;

    public void ApplyPresentationSettings(ProfilePresentationSettings settings)
    {
        _presentation = settings;
        _selectedBannerKey = ProfileBannerCatalog.Normalize(settings.BannerKey);
        _selectedShowcaseMode = settings.ShowcaseMode;
        _selectedCardFrame = settings.CardFrame;
        _selectedAvatarShape = settings.AvatarShape;
        ApplyCardVisibility(settings.ShowUsername, settings.ShowLevel, settings.ShowXp, settings.ShowPlaytime, settings.ShowSessions, settings.ShowStatus);
        _customBannerSourcePath = null;
        UpdateBannerPresentation();
        UpdateShowcasePresentation();
        UpdateCardOptionsPresentation();
        ApplyAvatarShapePreview();
        EnsurePresentationPreviewHooks();
    }

    private void EnsurePresentationPreviewHooks()
    {
        if (_presentationPreviewHooksAttached) return;
        _presentationPreviewHooksAttached = true;

        // The XAML AvatarShape_Click handler updates _selectedAvatarShape first; this second
        // handler then redraws the actual image border so Circle/Rounded/Square is visible before
        // the user saves rather than only after leaving and reopening the profile.
        CircleAvatarButton.Click += (_, _) => ApplyAvatarShapePreview();
        RoundedAvatarButton.Click += (_, _) => ApplyAvatarShapePreview();
        SquareAvatarButton.Click += (_, _) => ApplyAvatarShapePreview();
    }

    private void ApplyAvatarShapePreview() =>
        ProfileAvatarShapeStyle.Apply(AvatarPreviewBorder, _selectedAvatarShape, AvatarPreviewBorder.Width);
}
