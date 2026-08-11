using LanMountainDesktop.AirAppSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.AirAppTemplate;

/// <summary>
/// AirApp 入口。标记 <see cref="AirAppEntranceAttribute"/> 并继承 <see cref="AirAppBase"/>。
/// </summary>
[AirAppEntrance]
public sealed class AirApp : AirAppBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        _ = context;

        // ── 桌面组件 ──────────────────────────────────────────────────────
        // 组件在主进程内渲染，可放置到桌面网格。控件类型必须是 Avalonia Control。
        services.AddAirAppComponent<MyComponent>(
            new AirAppComponentOptions
            {
                ComponentId = "__COMPONENT_ID__",
                DisplayName = "__COMPONENT_NAME__",
                Description = "__COMPONENT_DESCRIPTION__",
                Category = "Custom",
                IconKey = "AppGeneric",
                MinWidthCells = 2,
                MinHeightCells = 2,
                AllowDesktopPlacement = true
            });

        // ── 窗口轻应用 ────────────────────────────────────────────────────
        // 窗口内容由 AirAppHost 独立进程承载（跨进程隔离）。
        // 组件内通过 AirAppComponentContext.OpenWindowAsync("__WINDOW_ID__") 打开。
        services.AddAirAppWindow<MyWindow>("__WINDOW_ID__", "__WINDOW_NAME__");

        // ── 声明式设置页（宿主自动生成）───────────────────────────────────
        // services.AddAirAppSettingsSection(
        //     "my-settings",
        //     "My Settings",
        //     section => section.AddToggle("enable_feature", "Enable Feature", defaultValue: true),
        //     iconKey: "Settings");
    }
}
