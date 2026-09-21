using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 桌面组件可达性守卫。这一层以前没有任何测试：注册表说"可以添加到桌面"、运行期注册表
/// 提供控件工厂、本地化表提供名字——三者只要有一处对不上，用户在组件库里看到的就是
/// 一个点开就失败或名字是英文原词的条目，而编译器和 UI 之外的测试都不会发现。
///
/// 覆盖四件事：
/// 1) 声明 AllowDesktopPlacement 的定义必须真的有运行期注册（否则库里有格子、创建必失败）。
/// 2) 运行期注册必须真的对应一个允许桌面摆放的定义（否则是永远拿不到的死注册）。
/// 3) 库里每个条目都要有 DisplayName 本地化键，且四种语言都能取到非空文案。
/// 4) 每个条目都能以 LibraryPreview 模式被构造、测量、排版，且不抛异常、尺寸非零。
/// </summary>
public sealed class DesktopComponentReachabilityTests
{
    private static readonly string[] Languages = ["zh-CN", "en-US", "ja-JP", "ko-KR"];

    [Fact]
    public void EveryDesktopPlacementAllowedDefinition_HasRuntimeRegistration()
    {
        var registered = DesktopComponentRuntimeRegistry.GetDefaultRegistrations()
            .Select(registration => registration.ComponentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = DesktopRegistry()
            .GetAll()
            .Where(definition => definition.AllowDesktopPlacement)
            .Select(definition => definition.Id)
            .Where(id => !registered.Contains(id))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AssertFailIfAny(missing);
    }

    [Fact]
    public void EveryDesktopRuntimeRegistration_ResolvesToADesktopAllowedDefinition()
    {
        var registry = DesktopRegistry();
        var offenders = new List<string>();

        foreach (var registration in DesktopComponentRuntimeRegistry.GetDefaultRegistrations())
        {
            if (!registry.TryGetDefinition(registration.ComponentId, out var definition))
            {
                offenders.Add($"{registration.ComponentId}: 没有对应的组件定义（注册会被静默丢弃）");
                continue;
            }

            if (!definition.AllowDesktopPlacement)
            {
                offenders.Add($"{registration.ComponentId}: 定义不允许桌面摆放（{definition.DisplayName}）");
            }
        }

        AssertFailIfAny(offenders);
    }

    [Fact]
    public void EveryAdvertisedComponent_HasLocalizedNameInAllLanguages()
    {
        var localization = new LocalizationService();
        var offenders = new List<string>();

        foreach (var descriptor in RuntimeRegistry().GetDesktopComponents())
        {
            var key = descriptor.DisplayNameLocalizationKey;
            var componentId = descriptor.Definition.Id;
            if (string.IsNullOrWhiteSpace(key))
            {
                offenders.Add($"{componentId}: 缺少 DisplayNameLocalizationKey，库里会直接显示英文原名 \"{descriptor.Definition.DisplayName}\"");
                continue;
            }

            foreach (var language in Languages)
            {
                var code = localization.NormalizeLanguageCode(language);
                var text = localization.GetString(code, key, "\0missing");
                if (text == "\0missing" || string.IsNullOrWhiteSpace(text))
                {
                    offenders.Add($"{componentId}: 键 {key} 在 {language} 里取不到文案");
                }
            }
        }

        AssertFailIfAny(offenders);
    }

    /// <summary>
    /// DesktopBrowser 不在这里造：它内部起 WebView2 运行时，会在当前线程上改 COM 套间模型，
    /// headless 线程因此在其后报 RPC_E_CHANGED_MODE（宿主里它只在真正的 STA UI 线程上创建，无此问题）。
    /// 这是测试线程环境限制，不是组件缺陷；除它之外的每个组件都在此受测。
    /// </summary>
    private static readonly string[] ThreadApartmentSensitiveComponents = ["DesktopBrowser"];

    public static TheoryData<string> DesktopComponentIds() => new(
        RuntimeRegistry()
            .GetDesktopComponents()
            .Select(descriptor => descriptor.Definition.Id)
            .Where(id => !ThreadApartmentSensitiveComponents.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// 每个组件单独一条用例。某个组件在 headless 线程上碰 COM 时会改动线程套间模型，
    /// 报错点落在测试体的 try/catch 之外（runner 收尾跑 dispatcher 时），
    /// 拆成单组件用例才能一眼定位是谁，而不是整批 46 个一起被带红。
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(DesktopComponentIds))]
    public void AdvertisedComponent_CanBeCreatedAndMeasuredInLibraryPreview(string componentId)
    {
        Assert.True(RuntimeRegistry().TryGetDescriptor(componentId, out var descriptor));

        var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
        var control = descriptor.CreateControl(
            cellSize: 64,
            settingsFacade.Region.GetTimeZoneService(),
            settingsFacade.Weather.GetWeatherInfoService(),
            new RecommendationDataService(),
            new CalculatorDataService(),
            settingsFacade,
            placementId: null,
            renderMode: DesktopComponentRenderMode.LibraryPreview);

        // 单独 Measure 一个未挂树的控件永远拿不到尺寸：宿主是用窗口承载它做布局的。
        var available = new Size(
            descriptor.Definition.MinWidthCells * 64,
            descriptor.Definition.MinHeightCells * 64);
        var host = new Window
        {
            Content = control,
            Width = available.Width,
            Height = available.Height,
        };
        host.Show();
        host.Measure(available);
        host.Arrange(new Rect(new Point(0, 0), available));

        Assert.True(
            control.Bounds.Width > 0 && control.Bounds.Height > 0,
            $"{componentId}: 布局后尺寸为零 ({control.Bounds.Width}x{control.Bounds.Height})");

        host.Close();
    }

    /// <summary>
    /// 免检清单不许烂掉：名单里的每一项现在仍得是库里真实存在的组件。
    /// </summary>
    [Fact]
    public void ThreadApartmentSensitiveComponents_AreStillAdvertised()
    {
        var advertised = RuntimeRegistry()
            .GetDesktopComponents()
            .Select(descriptor => descriptor.Definition.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var offenders = ThreadApartmentSensitiveComponents
            .Where(id => !advertised.Contains(id))
            .Select(id => $"{id}: 已不在组件库里，免检清单该删掉这一项")
            .ToList();

        AssertFailIfAny(offenders);
    }

    /// <summary>
    /// DisplayName 是 L(key, DisplayName) 的兜底文案，也就是本地化键缺失时用户真正看到的名字。
    /// 全仓 47 个定义里曾有 5 个把中文写死在这里，英/日/韩界面因此直接漏出中文。
    /// </summary>
    [Fact]
    public void ComponentDisplayNames_AreLanguageNeutralFallbacks()
    {
        var offenders = DesktopRegistry()
            .GetAll()
            .Where(definition => definition.DisplayName.Any(c => c >= 0x3000 && c <= 0x9fff))
            .Select(definition => $"{definition.Id}: \"{definition.DisplayName}\"")
            .ToList();

        AssertFailIfAny(offenders);
    }

    // Assert.Empty 会把集合截断成 ???，这里一次把完整清单抛出来，便于按条修复。
    private static void AssertFailIfAny(IReadOnlyList<string> offenders)
    {
        if (offenders.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{offenders.Count} 项不达要求：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static ComponentRegistry DesktopRegistry() => ComponentRegistry.CreateDefault();

    private static DesktopComponentRuntimeRegistry RuntimeRegistry() =>
        new DesktopComponentRuntimeRegistry(DesktopRegistry(), DesktopComponentRuntimeRegistry.GetDefaultRegistrations());
}
