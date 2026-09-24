using System;
using System.Linq;

using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 黄历"宜/忌"每天挑几条那套算法的行为钉（家在 <see cref="LunarCalendarService"/>）。
///
/// 收口前 <c>DateWidget</c> 与 <c>LunarCalendarWidget</c> 各抄一份逐字相同的 22 行，四张候选池表
/// 却早就共用同一个家了——所以漂开的只会是"怎么挑"，而两屏同一天都显示宜忌，少一条或多一条都不报错。
/// 这里既钉住判据，也**钉住一条实测到的现状缺陷**（见 <see cref="ChineseIllicitColumn_CurrentlyReachesThreeOfTwelveWords"/>）。
/// </summary>
public sealed class LunarDailySelectionTests
{
    /// <summary>5 与两个种子的步长互质，所以这块夹具池能整池取到——判据要量的是"怎么挑"，不是"池长不幸与步长有公约数"。</summary>
    private static readonly string[] Pool = ["甲", "乙", "丙", "丁", "戊"];

    [Fact]
    public void SameDateAndSalt_AlwaysPickTheSameItems()
    {
        var date = new DateTime(2026, 9, 24);

        var first = LunarCalendarService.BuildDailySelection(date, Pool, 3, LunarCalendarService.AuspiciousSalt, true);
        var second = LunarCalendarService.BuildDailySelection(date, Pool, 3, LunarCalendarService.AuspiciousSalt, true);

        Assert.Equal(first, second);
        Assert.Equal(3, first.Split(' ').Length);
    }

    [Fact]
    public void DifferentSalts_DressTheSameDayDifferently()
    {
        var date = new DateTime(2026, 9, 24);

        var yi = LunarCalendarService.BuildDailySelection(date, Pool, 3, LunarCalendarService.AuspiciousSalt, true);
        var ji = LunarCalendarService.BuildDailySelection(date, Pool, 3, LunarCalendarService.IllicitSalt, true);

        Assert.NotEqual(yi, ji);
    }

    [Fact]
    public void NeverRepeatsAnItem_HoweverManyAreAskedFor()
    {
        var items = LunarCalendarService.BuildDailySelection(
            new DateTime(2026, 9, 24), Pool, count: 99, LunarCalendarService.AuspiciousSalt, true).Split(' ');

        Assert.Equal(items.Length, items.Distinct().Count());
        Assert.InRange(items.Length, 1, Pool.Length);
    }

    [Fact]
    public void EmptyPoolOrNonPositiveCount_YieldsEmptyText()
    {
        var date = new DateTime(2026, 9, 24);

        Assert.Equal(string.Empty, LunarCalendarService.BuildDailySelection(date, [], 3, 17, true));
        Assert.Equal(string.Empty, LunarCalendarService.BuildDailySelection(date, Pool, 0, 17, true));
        Assert.Equal(string.Empty, LunarCalendarService.BuildDailySelection(date, Pool, -1, 17, true));
    }

    [Fact]
    public void ChineseUsesSpaceSeparators_OtherLocalesUseCommaSpace()
    {
        var date = new DateTime(2026, 9, 24);

        var zh = LunarCalendarService.BuildDailySelection(date, Pool, 2, 17, useChineseSpacing: true);
        var en = LunarCalendarService.BuildDailySelection(date, Pool, 2, 17, useChineseSpacing: false);

        Assert.DoesNotContain(",", zh);
        Assert.EndsWith(", " + zh.Split(' ')[1], en);
    }

    /// <summary>
    /// 钉住**现状缺陷**，不是认可它：步长 <c>(salt % (池长-1)) + 1</c> 与池长不互质时，绕圈只会踩到
    /// <c>池长 / gcd</c> 个下标。实测四张表（中文 12 条、英文 10 条）：
    /// 宜（种子 17，步长 7）与两张英文表（步长 9 / 3）都能整池取到；
    /// <b>中文"忌"（种子 29 → 步长 8，gcd(8,12)=4）永远只踩到 12 条里的 3 条</b>——
    /// 另外 9 条是写了但用户看不到的死词，而"忌"那一列最多只在 4 组三元组之间换。
    /// 为什么钉现状而不顺手改：改步长规则会让中文"忌"列的显示内容整个变掉（这是内容口径，得拍板），
    /// 挂 #G1-CB。谁把这张表加长或换种子，这条会立刻红并要人对一遍可达数。
    /// </summary>
    [Fact]
    public void ChineseIllicitColumn_CurrentlyReachesThreeOfTwelveWords()
    {
        Assert.Equal(3, MaxItemsPerDay(LunarCalendarService.JiCandidatesZh, LunarCalendarService.IllicitSalt, chinese: true));
        Assert.Equal(LunarCalendarService.JiCandidatesZh.Length, MaxItemsPerDay(LunarCalendarService.JiCandidatesZh, LunarCalendarService.AuspiciousSalt, chinese: true));
    }

    [Fact]
    public void EnglishTables_AndChineseAuspicious_ReachEveryWord()
    {
        Assert.Equal(LunarCalendarService.YiCandidatesZh.Length, MaxItemsPerDay(LunarCalendarService.YiCandidatesZh, LunarCalendarService.AuspiciousSalt, chinese: true));
        Assert.Equal(LunarCalendarService.YiCandidatesEn.Length, MaxItemsPerDay(LunarCalendarService.YiCandidatesEn, LunarCalendarService.AuspiciousSalt, chinese: false));
        Assert.Equal(LunarCalendarService.JiCandidatesEn.Length, MaxItemsPerDay(LunarCalendarService.JiCandidatesEn, LunarCalendarService.IllicitSalt, chinese: false));
    }

    /// <summary>
    /// "随便哪一天、向它要它装不下的那么多条，最多能给出几条不同的"。
    /// 跨天取并集是没意义的（换一天 cursor 就换一条陪集），要看的是**单天上限**。
    /// 分隔符必须跟着语言走：中文条目内不含空格（按 ' ' 数得对），英文条目含空格
    /// （"Major move" 用 ' ' 会数成两条，实测把 10 条数出 15），所以英文一律按界面上真用的 ", " 数。
    /// </summary>
    private static int MaxItemsPerDay(string[] pool, int salt, bool chinese)
    {
        var separator = chinese ? " " : ", ";
        var best = 0;
        for (var day = 0; day < 366; day++)
        {
            var text = LunarCalendarService.BuildDailySelection(
                new DateTime(2026, 1, 1).AddDays(day), pool, count: int.MaxValue, salt, useChineseSpacing: chinese);
            var count = text.Length == 0 ? 0 : text.Split(new[] { separator }, StringSplitOptions.None).Length;
            best = Math.Max(best, count);
        }

        return best;
    }
}
