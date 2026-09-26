using System;
using System.Collections.Generic;

using LanMountainDesktop.Services;
using LanMountainDesktop.Services.ClockAirApp;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 城市名表、选表口径与兜底写法收到一处之后的行为钉。
/// 这一族的错法全是"看着不像 bug"：表查不到就显示原始 id、兜底削多两个字就显示成空白，
/// 而两个组件的表以前是各抄一份的——补一颗城市只补一边，同一颗时区就在两块屏幕上显示成两个名字。
///
/// 2026-09-26 把 G1-BL 剩的三处不一致都定了（中文表里的 UTC 写字面 "UTC"、认不出的语言落英文、
/// ja/ko 两张表搬进本家让宿主也用上），所以这里从"钉住两端的分歧"改成"钉住两端必须一致"。
/// </summary>
public sealed class ClockCityNamesTests
{
    private static readonly string[] AllKeys =
    [
        "China Standard Time", "Asia/Shanghai",
        "GMT Standard Time", "Europe/London",
        "AUS Eastern Standard Time", "Australia/Sydney",
        "Eastern Standard Time", "America/New_York",
        "Tokyo Standard Time", "Asia/Tokyo",
        "UTC", "Etc/UTC"
    ];

    /// <summary>两端的入口都收同一个语言码；空码/null 只在上面那格单独钉（AirApp 的入参类型是非空串）。</summary>
    private static readonly string[] SharedLanguages = ["zh-CN", "en-US", "ja-JP", "ko-KR", "fr-FR", "de"];

    private static TimeZoneInfo Zone(string id) =>
        TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.Zero, id, id);

    [Theory]
    [InlineData("zh-CN", "Asia/Shanghai", "北京")]
    [InlineData("en-US", "Asia/Shanghai", "Beijing")]
    [InlineData("zh-CN", "China Standard Time", "北京")]
    [InlineData("ja-JP", "Asia/Tokyo", "東京")]        // 中文表这里是"东京"：这一格证明取的是日文表
    [InlineData("ja-JP", "Europe/London", "ロンドン")]
    [InlineData("ko-KR", "Asia/Tokyo", "도쿄")]
    [InlineData("fr-FR", "Asia/Shanghai", "Beijing")]  // 认不出的语言落英文，不再像以前那样落中文
    [InlineData("de", "UTC", "UTC")]
    [InlineData("", "Asia/Shanghai", "Beijing")]
    [InlineData(null, "Asia/Shanghai", "Beijing")]
    [InlineData("en-US", "asia/shanghai", "Beijing")]  // 键忽略大小写：盘上/系统给的写法不止一种
    [InlineData("zh-CN", "Etc/UTC", "UTC")]            // ①的定档：中文表里也写字面 UTC
    public void Resolution_PicksTheTableForThatLanguage(string? language, string id, string expected) =>
        Assert.Equal(expected, ClockCityNames.ResolveByLanguage(language, Zone(id)));

    [Theory]
    [InlineData("Asia/Choibalsan", "Choibalsan")]                 // 取最后一段
    [InlineData("W._Africa_Standard_Time", "W. Africa")]          // 下划线换空格、削掉 Standard Time
    [InlineData("AUS Eastern Daylight Time", "AUS Eastern")]      // 削掉 Daylight Time
    [InlineData("Samoa Time", "Samoa")]                           // 只剩一个孤零零的 Time 也要削掉
    [InlineData("W. Europe Standard Time", "W. Europe")]          // 表里没有这颗
    [InlineData("Time", "Time")]                                  // 削光了要退回原始 id，不许显示成空白
    public void FallbackName_CutsTheIdDownToSomethingReadable(string id, string expected) =>
        Assert.Equal(expected, ClockCityNames.FallbackName(Zone(id)));

    /// <summary>
    /// 两端必须给同一个答案：世界时钟/模拟时钟组件走 <see cref="ClockCityNames.ResolveByLanguage"/>，
    /// Clock AirApp 走 <see cref="ClockAirAppTimeFormatter.ResolveCityName"/>。
    /// 收口之前这里是有分歧的（UTC 一格、以及 fr 这类认不出的语言），那两格当时钉的是现状；
    /// 现在 12 颗时区 × 8 种语言码全部逐格对账，任何一侧再各走各的口径就会红。
    /// </summary>
    [Fact]
    public void TheHostWidgetEntryAndTheAirAppEntry_AgreeOnEveryZoneAndLanguage()
    {
        var disagreements = new List<string>();

        foreach (var language in SharedLanguages)
        {
            foreach (var key in AllKeys)
            {
                var host = ClockCityNames.ResolveByLanguage(language, Zone(key));
                var airApp = ClockAirAppTimeFormatter.ResolveCityName(Zone(key), language);
                if (!string.Equals(host, airApp, StringComparison.Ordinal))
                {
                    disagreements.Add($"语言 [{language}] 时区 [{key}]：宿主={host} AirApp={airApp}");
                }
            }
        }

        Assert.True(
            disagreements.Count == 0,
            $"{disagreements.Count} 格两端不一致：{Environment.NewLine}{string.Join(Environment.NewLine, disagreements)}");
    }

    /// <summary>
    /// 四张表同键同条数，且每一张的 UTC 都写字面 "UTC"。
    /// 钉的是"补一颗城市只补一边"和"某一语言表里 UTC 又本地化回一个词"这两种静默漂移。
    /// </summary>
    [Fact]
    public void AllFourTables_CarryTheSameKeysAndTheSameUtcEntry()
    {
        var tables = new (string Name, IReadOnlyDictionary<string, string> Table)[]
        {
            ("Chinese", ClockCityNames.ChineseTable),
            ("English", ClockCityNames.EnglishTable),
            ("Japanese", ClockCityNames.JapaneseTable),
            ("Korean", ClockCityNames.KoreanTable),
        };

        foreach (var (name, table) in tables)
        {
            Assert.Equal(AllKeys.Length, table.Count);
            foreach (var key in AllKeys)
            {
                Assert.True(table.ContainsKey(key), $"{name}表缺键 {key}");
            }

            Assert.Equal("UTC", table["UTC"]);
            Assert.Equal("UTC", table["Etc/UTC"]);
        }
    }

    /// <summary>语言认不出来时不许靠 LanguageCodes 的默认值（那是中文），这一格把这条钉死。</summary>
    [Fact]
    public void UnlistedLanguage_TakesTheEnglishTableEvenThoughTheDefaultIsChinese()
    {
        Assert.Same(ClockCityNames.EnglishTable, ClockCityNames.TableFor("fr-FR"));
        Assert.Same(ClockCityNames.JapaneseTable, ClockCityNames.TableFor("ja"));
        Assert.Same(ClockCityNames.KoreanTable, ClockCityNames.TableFor("ko-KR"));
        Assert.Same(ClockCityNames.ChineseTable, ClockCityNames.TableFor("zh"));
    }
}
