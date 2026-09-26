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
/// 两个入口在 2026-09-26 之前**选表的口径不一样**，这是 G1-BL 剩下的三处不一致，本轮都定了：
/// ① 中文表里 <c>UTC</c> / <c>Etc/UTC</c> 原本一边写"协调世界时"、一边写字面 <c>UTC</c> →
///    统一成字面 <c>UTC</c>。理由是这张表所在的那块界面里 "UTC" 还在当偏移前缀用
///    （<c>ClockAirAppTimeFormatter.FormatUtcOffset</c> 产出 <c>UTC+08:00</c>），
///    而且 AirApp 的四张语言表本来就全写字面 UTC——把中文那一格本地化，反而在一块屏幕上混出两种写法。
/// ② 选表口径统一成"认得 zh/ja/ko 就用对应表，其余一律英文表"。原来的两端分歧是
///    <c>LanguageCodes.Normalize</c> 对不认识的码回默认值（<c>Default = Chinese</c>），于是
///    <c>fr</c> 用户在 AirApp 里看到中文、在宿主组件里看到英文。这里**不看归一化结果**，
///    按语言族自己判，兜底给英文：表里的原文就是英文，给一个不认识的语种用户中文城市名最没法读。
/// ③ 覆盖补齐：ja/ko 两张表原来只在 Clock AirApp 那边有，宿主桌面上的 ja/ko 用户永远看到英文城市名
///    （G1-K 的欠账之一）。两张表搬进本家，两端共用，宿主也就能出 ja/ko。
/// </summary>
public static class ClockCityNames
{
    /// <summary>中文城市名表（12 条：Win 时区名与 IANA 名各一条，键忽略大小写）。</summary>
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
            ["UTC"] = "UTC",
            ["Etc/UTC"] = "UTC"
        };

    /// <summary>英文城市名表（12 条，与中文表同键）——也是"语言认不出来"时的兜底表。</summary>
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

    /// <summary>日文城市名表（12 条，同键）。原来只有 Clock AirApp 带着它，2026-09-26 搬进本家。</summary>
    public static readonly IReadOnlyDictionary<string, string> JapaneseTable =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["China Standard Time"] = "北京",
            ["Asia/Shanghai"] = "北京",
            ["GMT Standard Time"] = "ロンドン",
            ["Europe/London"] = "ロンドン",
            ["AUS Eastern Standard Time"] = "シドニー",
            ["Australia/Sydney"] = "シドニー",
            ["Eastern Standard Time"] = "ニューヨーク",
            ["America/New_York"] = "ニューヨーク",
            ["Tokyo Standard Time"] = "東京",
            ["Asia/Tokyo"] = "東京",
            ["UTC"] = "UTC",
            ["Etc/UTC"] = "UTC"
        };

    /// <summary>韩文城市名表（12 条，同键）。同上。</summary>
    public static readonly IReadOnlyDictionary<string, string> KoreanTable =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["China Standard Time"] = "베이징",
            ["Asia/Shanghai"] = "베이징",
            ["GMT Standard Time"] = "런던",
            ["Europe/London"] = "런던",
            ["AUS Eastern Standard Time"] = "시드니",
            ["Australia/Sydney"] = "시드니",
            ["Eastern Standard Time"] = "뉴욕",
            ["America/New_York"] = "뉴욕",
            ["Tokyo Standard Time"] = "도쿄",
            ["Asia/Tokyo"] = "도쿄",
            ["UTC"] = "UTC",
            ["Etc/UTC"] = "UTC"
        };

    /// <summary>
    /// 选表：按语言族认 zh/ja/ko，认不出来一律英文。
    /// 这里刻意不走 <see cref="LanguageCodes.Normalize"/>——它对不认识的码回默认值（中文），
    /// 那正是"fr 用户在 AirApp 看到中文城市名"的来源。
    /// </summary>
    public static IReadOnlyDictionary<string, string> TableFor(string? languageCode)
    {
        var code = (languageCode ?? string.Empty).Trim().ToLowerInvariant();
        return code switch
        {
            "zh" or "zh-cn" => ChineseTable,
            "ja" or "ja-jp" => JapaneseTable,
            "ko" or "ko-kr" => KoreanTable,
            _ when code.StartsWith("zh", StringComparison.Ordinal) => ChineseTable,
            _ when code.StartsWith("ja", StringComparison.Ordinal) => JapaneseTable,
            _ when code.StartsWith("ko", StringComparison.Ordinal) => KoreanTable,
            _ => EnglishTable,
        };
    }

    /// <summary>两端共用的取城市名入口：世界时钟/模拟时钟组件与 Clock AirApp 都走这一条。</summary>
    public static string ResolveByLanguage(string? languageCode, TimeZoneInfo timeZone) =>
        Lookup(TableFor(languageCode), timeZone);

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
