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
/// 这里既钉住判据，也钉住一条**已修**的缺陷（见 <see cref="EveryCandidateReachesTheBoard_WithinOneYear"/>：
/// 修之前中文"忌"一年只上屏 12 条里的 3 条）。
/// </summary>
public sealed class LunarDailySelectionTests
{
    /// <summary>夹具池只有 5 条——步长由家保证与池长互质，所以这块池能整池取到。</summary>
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
    /// 四张候选池（中文 12 条、英文 10 条）一年内必须<b>整池都上过屏</b>。
    /// 2026-09-25 之前这条不成立：步长 <c>(salt % (池长-1)) + 1</c> 与池长不互质时，绕圈只踩到
    /// <c>池长 / gcd</c> 个下标，中文"忌"（种子 29 → 步长 8，gcd(8,12)=4）12 条只出现过 3 条，
    /// 另外 9 条是写了用户永远看不到的词（当时登记为 #G1-CB 等拍板，本次按"互质步长"改掉）。
    /// 家改成"从种子导出的起点往上找与池长互质的最小步长"之后，这里正向钉住可达数；
    /// 谁把表加长或换种子导致又出现公约数，这条会红并报出实测数（而不是像以前那样悄悄少显示几条）。
    /// </summary>
    [Theory]
    [InlineData(true, 12)]
    [InlineData(false, 10)]
    public void EveryCandidateReachesTheBoard_WithinOneYear(bool chinese, int expectedPoolLength)
    {
        var yiPool = chinese ? LunarCalendarService.YiCandidatesZh : LunarCalendarService.YiCandidatesEn;
        var jiPool = chinese ? LunarCalendarService.JiCandidatesZh : LunarCalendarService.JiCandidatesEn;

        Assert.Equal(expectedPoolLength, yiPool.Length);
        Assert.Equal(expectedPoolLength, jiPool.Length);
        Assert.Equal(yiPool.Length, MaxItemsPerDay(yiPool, LunarCalendarService.AuspiciousSalt, chinese));
        Assert.Equal(jiPool.Length, MaxItemsPerDay(jiPool, LunarCalendarService.IllicitSalt, chinese));
    }

    [Fact]
    public void FixingReachability_DoesNotChangeHowManyLinesShowUpPerDay()
    {
        // 单天仍然只给"要的那几条"（界面那一列的宽度没变），变的是一年内换着出现哪些。
        var text = LunarCalendarService.BuildDailySelection(
            new DateTime(2026, 9, 25), LunarCalendarService.JiCandidatesZh, count: 4,
            LunarCalendarService.IllicitSalt, useChineseSpacing: true);

        Assert.Equal(4, text.Split(' ').Length);
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
