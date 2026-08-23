using GrevHome.Profiles;

namespace GrevHome.Views;

public partial class ProfileEditView
{
    public void ApplyPresentationSettings(ProfilePresentationSettings settings)
    {
        _presentation = settings;
        _selectedBannerKey = ProfileBannerCatalog.Normalize(settings.BannerKey);
        _selectedShowcaseMode = settings.ShowcaseMode;
        _customBannerSourcePath = null;
        UpdateBannerPresentation();
        UpdateShowcasePresentation();
    }
}
