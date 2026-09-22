using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 黑夜档判定与"只在翻了才重画"这条判据的家唯一的证据。
///
/// 为什么值得单独钉：这条判据错法有两个方向，且都不报错——
/// 少了 <c>HasValue</c> 那一步，第一次进来就被判成"没变"，面板整块不画；
/// 比较反了，则每次尺寸变化/悬停都重分配一批画刷。此前它被逐字抄在 4 个组件里
/// （另两个带 <c>force</c> 口子），抄歪只能靠肉眼。
/// </summary>
public sealed class ComponentThemeModeTests
{

    /// <summary>
    /// 载体用 Window：它是这仓里唯一能就地写 RequestedThemeVariant 的元素（Control 没有这个属性，
    /// Avalonia 12 也没暴露公开的附加属性可写），而 ResolveIsNight 只看 ActualThemeVariant，不必显示窗口。
    /// 窗口不显示、也不关：Avalonia 12 的 Window 没有 IDisposable，未显示的顶层不进 dispatcher。
    /// </summary>
    private static Window Themed(ThemeVariant variant) => new() { RequestedThemeVariant = variant };

    [AvaloniaFact]
    public void ResolveIsNight_FollowsTheControlsOwnThemeVariant()
    {
        var dark = Themed(ThemeVariant.Dark);
        var light = Themed(ThemeVariant.Light);

        Assert.True(ComponentThemeMode.ResolveIsNight(dark));
        Assert.False(ComponentThemeMode.ResolveIsNight(light));
    }

    [AvaloniaFact]
    public void RefreshNightVisualIfChanged_FirstPass_AlwaysDraws()
    {
        bool? applied = null;
        var painted = new List<bool>();

        ComponentThemeMode.RefreshNightVisualIfChanged(
            Themed(ThemeVariant.Dark),
            ref applied,
            isNight => painted.Add(isNight));

        // 字段为空＝从没画过，这时"档位相同"不成立，必须画一次。
        Assert.Single(painted);
        Assert.True(painted[0]);
        Assert.True(applied);
    }

    [AvaloniaFact]
    public void RefreshNightVisualIfChanged_DoesNotRedraw_WhileTheModeIsUnchanged()
    {
        bool? applied = null;
        var painted = new List<bool>();
        var control = Themed(ThemeVariant.Dark);

        ComponentThemeMode.RefreshNightVisualIfChanged(control, ref applied, isNight => painted.Add(isNight));
        ComponentThemeMode.RefreshNightVisualIfChanged(control, ref applied, isNight => painted.Add(isNight));

        Assert.Single(painted);
    }

    [AvaloniaFact]
    public void RefreshNightVisualIfChanged_RedrawsWhenTheModeFlips()
    {
        bool? applied = null;
        var painted = new List<bool>();
        var control = Themed(ThemeVariant.Dark);

        ComponentThemeMode.RefreshNightVisualIfChanged(control, ref applied, isNight => painted.Add(isNight));
        control.RequestedThemeVariant = ThemeVariant.Light;
        ComponentThemeMode.RefreshNightVisualIfChanged(control, ref applied, isNight => painted.Add(isNight));

        Assert.Equal(2, painted.Count);
        Assert.True(painted[0]);
        Assert.False(painted[1]);
        Assert.False(applied);
    }

    [AvaloniaFact]
    public void RefreshNightVisualIfChanged_ForceIgnoresTheMemo()
    {
        // 诗句/白板那两类要能在"档位没变但内容要重排"时强制重画。
        bool? applied = null;
        var painted = 0;
        var control = Themed(ThemeVariant.Dark);

        ComponentThemeMode.RefreshNightVisualIfChanged(control, ref applied, _ => painted++);
        ComponentThemeMode.RefreshNightVisualIfChanged(control, ref applied, _ => painted++, force: true);

        Assert.Equal(2, painted);
    }
}
