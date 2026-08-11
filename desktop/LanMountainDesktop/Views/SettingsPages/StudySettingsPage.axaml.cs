using Avalonia.Controls;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[AirAppSettingsPageInfo(
    "study",
    "Study",
    AirAppSettingsPageCategory.Appearance,
    IconKey = "Hourglass",
    SortOrder = 19,
    TitleLocalizationKey = "settings.study.title",
    DescriptionLocalizationKey = "settings.study.description")]
public partial class StudySettingsPage : AirAppSettingsPageBase
{
    public StudySettingsPage()
        : this(Design.IsDesignMode ? CreateDesignTimeViewModel() : CreateDefaultViewModel())
    {
    }

    public StudySettingsPage(StudySettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public StudySettingsPageViewModel ViewModel { get; }

    private static StudySettingsPageViewModel CreateDefaultViewModel()
    {
        var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
        return new StudySettingsPageViewModel(settingsFacade);
    }

    private static StudySettingsPageViewModel CreateDesignTimeViewModel()
    {
        return CreateDefaultViewModel();
    }
}
