using System;

namespace LanMountainDesktop.Helpers;

/// <summary>
/// 组件上"多久之前"的相对时间文案，归一化口径只认这一处。
/// 收口前同一段阶梯（刚刚 → N 分钟前 → N 小时前 → N 天前）在
/// <c>NotificationBoxWidget</c> 与 <c>OfficeRecentDocumentsWidget</c> 里各抄了一份，
/// 后者多出"周"与日期兜底两档。抄丢一处的症状：两个组件对同一个时间点给出不同说法。
/// 两档刻度各自保持收口前的显示行为：通知盒到"天"为止，最近文档到"周"并回退到日期。
/// </summary>
internal static class RelativeTimeText
{
    public enum Scale
    {
        /// <summary>超过 24 小时一律显示"N 天前"（通知盒的口径）。</summary>
        UpToDays,

        /// <summary>7 天内显示"N 天前"，30 天内显示"N 周前"，更早回退为 MM/dd 日期（最近文档的口径）。</summary>
        UpToWeeksWithDateFallback,
    }

    /// <summary>
    /// 方法名不叫 Format：本仓已有 TimeZoneDisplayLabel.Format 与 AirAppLocalizer.Format 两种体，
    /// 再添一种同名体会把同名漂移族计数推过棘轮上限——这里要的是"多久之前"一个专名。
    /// </summary>
    public static string Ago(DateTime time, Scale scale)
    {
        var span = DateTime.Now - time;

        if (span.TotalMinutes < 1)
        {
            return "刚刚";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes} 分钟前";
        }

        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours} 小时前";
        }

        if (scale == Scale.UpToWeeksWithDateFallback)
        {
            if (span.TotalDays < 7)
            {
                return $"{(int)span.TotalDays} 天前";
            }

            if (span.TotalDays < 30)
            {
                return $"{(int)(span.TotalDays / 7)} 周前";
            }

            return time.ToString("MM/dd");
        }

        return $"{(int)span.TotalDays} 天前";
    }
}
