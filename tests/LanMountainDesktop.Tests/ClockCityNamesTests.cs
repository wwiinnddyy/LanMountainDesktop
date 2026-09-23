using System;
using System.Collections.Generic;

using LanMountainDesktop.Services;
using LanMountainDesktop.Services.ClockAirApp;
using LanMountainDesktop.Shared.Contracts.Localization;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 城市名表与它的兜底写法收到一处之后的行为钉。
/// 这一族的错法全是"看着不像 bug"：表查不到就显示原始 id、兜底削多两个字就显示成空白，
/// 而两个组件的表以前是各抄一份的——补一颗城市只补一边，同一颗时区就在两块屏幕上显示成两个名字。
/// </summary>
public sealed class ClockCityNamesTests
{
    private static TimeZoneInfo Zone(string id) =>
        TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.Zero, id, id);

    [Theory]
    [InlineData(true, "Asia/Shanghai", "北京")]
    [InlineData(false, "Asia/Shanghai", "Beijing")]
    [InlineData(true, "China Standard Time", "北京")]
    [InlineData(false, "asia/shanghai", "Beijing")]              // 键忽略大小写：盘上/系统给的写法不止一种
    [InlineData(false, "Etc/UTC", "UTC")]
    public void HostWidgetResolution_PrefersTheTableOfTheRightLanguage(bool isChinese, string id, string expected) =>
        Assert.Equal(expected, ClockCityNames.ResolveForHostWidget(isChinese, Zone(id)));

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
    /// 两个入口的表选择口径不一样，是既有事实，这一格把它钉住：
    /// AirApp 按归一化语言取各自表（多 ja/ko），宿主组件只有"中文 / 其余一律英文"两档。
    /// 于是同一个"表里没有的语言"（法语）两端显示不同——AirApp 跟着 <c>LanguageCodes.Default</c> 落回中文，
    /// 宿主组件把它算成"非中文"走英文字表。这一条挂在 G1-BL 里等拍板，先不许被顺手改掉。
    /// </summary>
    [Fact]
    public void TheTwoEntriesPickTheirTableByDifferentRules_AndThatIsVisibleForAnUnlistedLanguage()
    {
        Assert.Equal("北京", ClockAirAppTimeFormatter.ResolveCityName(Zone("Asia/Shanghai"), LanguageCodes.Japanese));
        Assert.Equal("Beijing", ClockCityNames.ResolveForHostWidget(
            isChinese: false, Zone("Asia/Shanghai")));
        Assert.Equal("北京", ClockAirAppTimeFormatter.ResolveCityName(Zone("Asia/Shanghai"), "fr-FR"));
    }

    /// <summary>
    /// G1-BL 的分歧锚点：中文表里 UTC 这一格两边取值不同（这边"协调世界时"、AirApp 那边写字面 "UTC"）。
    /// 各留各的是暂时的——拍板统一成哪个词之前，这条判据保证没人顺手把它并掉。
    /// </summary>
    [Fact]
    public void UtcZone_StillDisagreesBetweenTheHostWidgetAndTheAirApp()
    {
        Assert.Equal("协调世界时", ClockCityNames.ResolveForHostWidget(isChinese: true, Zone("UTC")));
        Assert.Equal("UTC", ClockAirAppTimeFormatter.ResolveCityName(Zone("UTC"), LanguageCodes.Chinese));
    }

    [Fact]
    public void HostWidgetTables_CarryBothTheWinnamesAndTheIanaNames()
    {
        Assert.Equal(12, ClockCityNames.ChineseTable.Count);
        Assert.Equal(ClockCityNames.ChineseTable.Keys, ClockCityNames.EnglishTable.Keys);
        Assert.Contains("Etc/UTC", ClockCityNames.ChineseTable);
        Assert.Contains("Australia/Sydney", ClockCityNames.EnglishTable);
    }

    /// <summary>
    /// 英文字表只有一份了：AirApp 的英文子表就是家的 <c>EnglishTable</c>（12 条逐键等值，所以并过去行为不变）。
    /// 这一格钉的是"别再抄第二份英文表"——新加一颗城市只补一边，就是同一颗时区在两端显示成两个城市名。
    /// </summary>
    [Theory]
    [InlineData("Asia/Shanghai", "Beijing")]
    [InlineData("China Standard Time", "Beijing")]
    [InlineData("Australia/Sydney", "Sydney")]
    [InlineData("Etc/UTC", "UTC")]
    public void AirAppEnglishPath_AndTheHostWidgetShareTheSameTable(string id, string expected)
    {
        Assert.Equal(expected, ClockCityNames.ResolveForHostWidget(isChinese: false, Zone(id)));
        Assert.Equal(expected, ClockAirAppTimeFormatter.ResolveCityName(Zone(id), LanguageCodes.English));
    }
}
