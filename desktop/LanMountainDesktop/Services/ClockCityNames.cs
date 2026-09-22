using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Services;

/// <summary>
/// 世界时钟"时区 → 城市名"这张表与它的兜底写法，唯一一份。
///
/// 收口前有 3 份：世界时钟与模拟时钟两个组件各抄了一份**内容完全相同**的 12+12 条表
/// （一份写成字面中文、一份写成 Unicode 转义，所以逐字普查看不见它——数据漂开时也不报红），
/// 外加两份逐字相同的 20 行 ResolveCityName；Clock AirApp 的 ClockAirAppTimeFormatter
/// 还有第三份同样的兜底写法。症状都是"错了不报错"：
/// 同一颗时区在一块屏幕上显示"北京"、在另一块上显示 Asia/Shanghai。
///
/// 两个入口分开是因为**表选择口径本来就不一致**，这一笔不替它们统一：
/// 宿主组件是"中文用中文字表、其余一律英文字表"，AirApp 是"按归一化语言取各自表"（多 ja/ko）。
/// 而中文表里 UTC / Etc/UTC 两条两边取值不同（这边"协调世界时"、AirApp 那边写字面 UTC）——
/// 统一到哪个词要先拍板（G1-BL），所以现在各留各的表，只把兜底写法与这两张表收成一家。
/// </summary>
public static class ClockCityNames
{
    /// <summary>宿主时钟组件的中文城市名表（12 条：Win 时区名与 IANA 名各一条，键忽略大小写）。</summary>
    public static readonly IReadOnlyDictionary<string, string> ChineseTable =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["China Standard Time"] = "北京",
            ["Asia/Shanghai"] = "北京",
            ["GMT Standard Time"] = "伦敦",
            ["Europe/London"] = "伦敦",
            ["AUS Eastern Standard Time"] = "悉尼",
            ["Australia/Sydney"] = "悉尼",
            ["Eastern Standard Time"] = "纽约",
            ["America/New_York"] = "纽约",
            ["Tokyo Standard Time"] = "东京",
            ["Asia/Tokyo"] = "东京",
            ["UTC"] = "协调世界时",
            ["Etc/UTC"] = "协调世界时"
        };

    /// <summary>宿主时钟组件的英文城市名表（12 条，与中文表同键）。</summary>
    public static readonly IReadOnlyDictionary<string, string> EnglishTable =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["China Standard Time"] = "Beijing",
            ["Asia/Shanghai"] = "Beijing",
            ["GMT Standard Time"] = "London",
            ["Europe/London"] = "London",
            ["AUS Eastern Standard Time"] = "Sydney",
            ["Australia/Sydney"] = "Sydney",
            ["Eastern Standard Time"] = "New York",
            ["America/New_York"] = "New York",
            ["Tokyo Standard Time"] = "Tokyo",
            ["Asia/Tokyo"] = "Tokyo",
            ["UTC"] = "UTC",
            ["Etc/UTC"] = "UTC"
        };

    /// <summary>
    /// 宿主组件的取表口径：只有中文走中文字表，其他语言一律英文字表。
    /// 判定由调用方传进来（组件那边是 <c>ILocalizationService.IsChineseLanguage</c>），
    /// 家不依赖服务，这张表才单测得了。
    /// </summary>
    public static string ResolveForHostWidget(bool isChinese, TimeZoneInfo timeZone) =>
        Lookup(isChinese ? ChineseTable : EnglishTable, timeZone);

    /// <summary>表里查得到就用表里的，查不到走 <see cref="FallbackName"/>。</summary>
    public static string Lookup(IReadOnlyDictionary<string, string> table, TimeZoneInfo timeZone) =>
        table.TryGetValue(timeZone.Id, out var cityName) ? cityName : FallbackName(timeZone);

    /// <summary>
    /// 表里没有这颗时区时，把 <c>Asia/Choibalsan</c> 这类 id 变成能看的名字：
    /// 取最后一段、下划线换空格、去掉 <c>Standard/Daylight Time</c> 与孤零零的 <c>Time</c>；
    /// 全被削光了就退回原始 id（宁可生涩，也不要显示成空白）。
    /// </summary>
    public static string FallbackName(TimeZoneInfo timeZone)
    {
        var normalized = timeZone.Id;
        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < normalized.Length - 1)
        {
            normalized = normalized[(slashIndex + 1)..];
        }

        normalized = normalized.Replace('_', ' ').Trim();
        normalized = normalized
            .Replace("Standard Time", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Daylight Time", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Time", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(normalized) ? timeZone.Id : normalized;
    }
}
