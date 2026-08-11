using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[AirAppSettingsPageInfo(
    "notifications",
    "Notifications",
    AirAppSettingsPageCategory.Components,
    IconKey = "Bell",
    SortOrder = 5,
    TitleLocalizationKey = "settings.notifications.title",
    DescriptionLocalizationKey = "settings.notifications.description")]
public partial class NotificationSettingsPage : AirAppSettingsPageBase
{
    public NotificationSettingsPage()
        : this(new NotificationSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public NotificationSettingsPage(NotificationSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public NotificationSettingsPageViewModel ViewModel { get; }
}
