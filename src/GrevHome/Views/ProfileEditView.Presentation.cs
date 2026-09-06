using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class ProfileEditView
{
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
    }
}
