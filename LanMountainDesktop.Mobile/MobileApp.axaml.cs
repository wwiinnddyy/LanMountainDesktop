using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LanMountainDesktop.Mobile.ViewModels;
using LanMountainDesktop.Mobile.Views;

namespace LanMountainDesktop.Mobile;

/// <summary>
/// 移动端应用入口。以单视图生命周期承载组件面板。
/// 与桌面宿主 (LanMountainDesktop.App) 共享契约、外观与组件运行时，
/// 但不包含桌面层、托盘、进程外插件等桌面专属能力。
/// </summary>
public partial class MobileApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new WidgetPanelView
            {
                DataContext = new WidgetPanelViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
