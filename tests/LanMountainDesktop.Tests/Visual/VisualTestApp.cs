using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

using LanMountainDesktop;

using AppBuilding = Avalonia.AppBuilder;

[assembly: AvaloniaTestApplication(typeof(LanMountainDesktop.Tests.Visual.VisualTestApp))]

namespace LanMountainDesktop.Tests.Visual;

/// <summary>
/// 视觉回归用的应用宿主入口。
///
/// 为什么必须新写一个入口：headless 测试默认只起一个裸 <see cref="Application"/>，
/// 于是 App.axaml 里的 FluentAvalonia 主题、Design* 圆角令牌、SettingsCardStyles 全都不存在，
/// 任何"渲染出来看看"的断言都只能在无样式环境下跑，等于没测。
///
/// 这里刻意用 SetupWithoutStarting 语义（headless 会话不会调 OnFrameworkInitializationCompleted），
/// 所以托盘、IPC 宿主、真实窗口这些主机副作用都不会发生 —— 只有资源和样式是真的。
/// </summary>
public static class VisualTestApp
{
    public static AppBuilding BuildAvaloniaApp() => AppBuilding
        .Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = true
        })
        .WithInterFont();
}
