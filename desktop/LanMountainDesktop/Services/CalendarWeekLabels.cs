using System;

namespace LanMountainDesktop.Services;

/// <summary>
/// 日历格子的星期表头标签，唯一一份。此前日期组件与月历组件各存一份逐字相同的 7 元数组，
/// 还各自写一遍"中文取中表、否则取英表"的分支——只改一边的症状是：
/// 两个日历组件的同一列显示成不同的字。
/// 英文表里 T/W/F/S 有重复字母，那是这套标签本来的样子（列位靠顺序，不靠唯一性）。
/// </summary>
public static class CalendarWeekLabels
{
    /// <summary>日 一 二 三 四 五 六（周日打头，与日历控件的列序一致）。</summary>
    public static readonly string[] Zh = ["\u65e5", "\u4e00", "\u4e8c", "\u4e09", "\u56db", "\u4e94", "\u516d"];

    /// <summary>S M T W T F S。</summary>
    public static readonly string[] En = ["S", "M", "T", "W", "T", "F", "S"];

    /// <summary>组件只说"是不是中文"，别再各自抄一遍三元分支。</summary>
    public static string[] For(bool isChinese) => isChinese ? Zh : En;
}
