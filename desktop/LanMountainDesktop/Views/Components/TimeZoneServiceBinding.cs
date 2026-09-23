using System;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件与应用级时区服务之间那对订阅/退订只认这一处。
/// </summary>
/// <remarks>
/// <see cref="TimeZoneService"/> 是长命的（<c>SettingsDomainServices</c> 里一个字段，事件本身没有任何退订兜底），
/// 控件是短命的：谁忘了退订，服务就替它一直持有那个控件——连整棵已经从桌面分离下来的 visual tree 一起。
/// 收口前这件事在 10 个组件里各抄了一遍（10 份 Set + 10 份 Clear 方法体），抄漏一边就是泄漏。
/// 两个方法都返回新的字段值，语义与原来逐字一致：换服务时先退旧的再订新的，退订时不刷新。
/// 顺带去掉一处潜在崩溃：原来 <c>SetTimeZoneService(null)</c> 会在 <c>+=</c> 那行抛 <c>NullReferenceException</c>。
/// </remarks>
public static class TimeZoneServiceBinding
{
    /// <summary>退掉 <paramref name="current"/>、订上 <paramref name="next"/>，返回新的字段值。</summary>
    public static TimeZoneService? Replace(
        TimeZoneService? current,
        TimeZoneService? next,
        EventHandler onChanged)
    {
        Unsubscribe(current, onChanged);

        if (next is not null)
        {
            next.TimeZoneChanged += onChanged;
        }

        return next;
    }

    /// <summary>
    /// 换绑之后必须立刻重画一次：这条顺序现在由家管（先 Replace、再刷新），
    /// 八个组件不再各写一遍六行。用 ref 传字段是刻意的——刷新得看到换好之后的字段值，
    /// 若在赋值之前刷新就会拿旧时区重画（症状：时区改了但表盘要到下一次变化才对，且不报错）。
    /// 刷新只发生一次：多刷一次会在换绑那一下重排整块面板。
    /// </summary>
    public static void Attach(
        ref TimeZoneService? field,
        TimeZoneService? next,
        EventHandler onChanged,
        Action refreshAfterChange)
    {
        field = Replace(field, next, onChanged);
        refreshAfterChange();
    }
    /// <summary>退掉 <paramref name="current"/> 并返回 null；本来没订过就是空操作。</summary>
    public static TimeZoneService? Clear(TimeZoneService? current, EventHandler onChanged)
    {
        Unsubscribe(current, onChanged);
        return null;
    }

    private static void Unsubscribe(TimeZoneService? service, EventHandler onChanged)
    {
        if (service is not null)
        {
            service.TimeZoneChanged -= onChanged;
        }
    }
}
