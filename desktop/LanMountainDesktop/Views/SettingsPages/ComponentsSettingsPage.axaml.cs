using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "components",
    "Components",
    SettingsPageCategory.Components,
    IconKey = "AppFolder",
    SortOrder = 20,
    TitleLocalizationKey = "settings.components.title",
    DescriptionLocalizationKey = "settings.components.description")]
public partial class ComponentsSettingsPage : SettingsPageBase
{
    public ComponentsSettingsPage()
        : this(new ComponentsSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public ComponentsSettingsPage(ComponentsSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
        InitScreenAspectRatio();
    }

    private void InitScreenAspectRatio()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            var screen = topLevel.Screens.Primary ?? topLevel.Screens.All.FirstOrDefault();
            if (screen is not null && screen.Bounds.Height > 0)
            {
                ViewModel.ScreenAspectRatio = (double)screen.Bounds.Width / screen.Bounds.Height;
            }
        }
        catch
        {
            // 无法获取屏幕信息时保持默认 16:9
        }
    }

    public ComponentsSettingsPageViewModel ViewModel { get; }
}
