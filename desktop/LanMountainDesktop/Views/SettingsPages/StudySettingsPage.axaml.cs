using Avalonia.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "study",
    "Study",
    SettingsPageCategory.Appearance,
    IconKey = "Hourglass",
    SortOrder = 19,
    TitleLocalizationKey = "settings.study.title",
    DescriptionLocalizationKey = "settings.study.description")]
public partial class StudySettingsPage : SettingsPageBase
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
