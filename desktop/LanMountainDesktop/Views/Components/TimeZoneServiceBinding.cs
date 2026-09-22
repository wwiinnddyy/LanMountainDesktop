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
