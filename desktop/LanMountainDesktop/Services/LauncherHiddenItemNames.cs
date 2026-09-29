using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LanMountainDesktop.Services;

/// <summary>
/// 启动台"隐藏项"的键与显示名，规则只认这一家。
///
/// 兜底显示名：斜杠统一、取末段文件名、去掉扩展名，取不出像样的名字就退回原键，整条为空才是 <c>"Unknown"</c>。
/// 此前设置页（<c>LauncherSettingsPageViewModel</c>）与桌面分页（<c>MainWindow.DesktopPaging</c>）
/// 各抄一份逐字相同的 9 行——改了设置页那一份，桌面叠加层上的隐藏项标签不会跟着变，
/// 同一个隐藏项在两处显示成两个名字。
///
/// 键的清洗（<see cref="NormalizeKey"/>）与一组键的去重排序（<see cref="NormalizeKeys"/>）也在这里：
/// 收口前是三份——写盘侧 <c>LauncherSettingsService.NormalizeKeys</c>、设置页读侧自己排一遍序再去重、
/// 加上 <c>NormalizeLauncherHiddenKey</c> 两份同语义不同写法（一条表达式体、一条块体，
/// 所以"逐字相同"那把尺子根本看不见这一族）。为什么要一家：这个键是**磁盘上的值**
/// （<c>launcher-settings.json</c> 里的列表），写的时候去不去重、按什么排，
/// 决定的是读侧两行会不会长成同一条；两份口径漂开的症状不是崩，而是"隐藏了的项目还在启动台里出现一次"。
/// </summary>
public static class LauncherHiddenItemNames
{
    public static string FallbackDisplayName(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Unknown";
        }

        var normalized = key!.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        return string.IsNullOrWhiteSpace(fileName)
            ? key
            : fileName;
    }

    /// <summary>一个隐藏项的键长什么样：只去首尾空白；空的一律成空串（调用方按"没有这个键"处理）。</summary>
    public static string NormalizeKey(string? key)
        => string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();

    /// <summary>
    /// 一组键写盘/读出前的样子：去空白、去重、按 <c>OrdinalIgnoreCase</c> 排——
    /// 大小写不敏感是必须的，Windows 上 <c>C:\\Apps\\X</c> 与 <c>C:\\apps\\x</c> 是同一个目录，
    /// 让两条都留下就是"同一个项目显示两行"。
    /// </summary>
    public static List<string> NormalizeKeys(IEnumerable<string?>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Select(NormalizeKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
