using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using TKCode.Arc;

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
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = false
        })
        .WithInterFont()
        // Arc 设计令牌（Phase 2.2 起 SettingsCardStyles 直接读 ArcDesignTokenResource）：
        // 测试宿主也必须注册 Arc，否则令牌解析不到，卡片背景全空。
        // §10.1：21 个设置页用 arc:SettingsGroup/SettingsRow（TKCode.Arc.Blocks），
        // ControlTheme 必须 .UseArcBlocks() 注册；只注册 Controls 会让 Blocks 控件"有对象无模板"、
        // 子元素不进可视化树——headless 里就是"整页空白"的同款假绿。与 Program.cs 保持一致。
        .UseArc(arc => arc.UseArcControls().UseArcBlocks());
}
