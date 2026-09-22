using Avalonia.Threading;

using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 每日一词的自动刷新档位，唯一一份读取：1x1 与 2x2 两块面板读的是<b>同一对设置键</b>
/// （<c>DailyWordAutoRefreshEnabled</c> / <c>DailyWordAutoRefreshIntervalMinutes</c>），
/// 所以"默认 6 小时"这个数只能有一处。2026-09-23 收口前的实测形态是：两个组件的方法体各写一遍
/// （13 行逐字相同），编辑器注册表里还有第三份 <c>DefaultInterval = 360</c>。
/// 漂了不报错，症状是面板按一个间隔刷、设置页显示另一个档位。
/// </summary>
internal static class DailyWordAutoRefresh
{
    internal const int DefaultIntervalMinutes = 360;

    /// <summary>
    /// 读档位并按它重排刷新表。回传"这次读到的开关"，因为组件要把它存进自己的字段用于按钮态。
    /// 读不到设置（首次启动、盘上的快照坏了）就按默认档跑：面板照常显示，只是这次不刷新——
    /// 两边原本都是这个兜底，收进家之后就只有一处能改它。
    /// </summary>
    internal static bool Apply(
        IComponentInstanceSettingsStore settings,
        DispatcherTimer refreshTimer,
        bool isAttached)
    {
        var enabled = true;
        var intervalMinutes = DefaultIntervalMinutes;

        try
        {
            var snapshot = settings.Load();
            enabled = snapshot.DailyWordAutoRefreshEnabled;
            intervalMinutes = Models.RefreshIntervalCatalog.Normalize(
                snapshot.DailyWordAutoRefreshIntervalMinutes,
                DefaultIntervalMinutes);
        }
        catch
        {
            // Keep fallback defaults.
        }

        ComponentRefreshLifetime.Reschedule(refreshTimer, isAttached, enabled, intervalMinutes);
        return enabled;
    }
}
