using System;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 时区标签的形状钉。夹具一律用 <c>TimeZoneInfo.CreateCustomTimeZone</c> 自己造的时区，
/// 不去查系统时区库——"这台机器上有没有 America/Los_Angeles"不该决定门是红是绿。
/// 钉得住的是：符号、小时与分钟各两位、半小时/四十五分钟这类非整点偏移、零偏移算正、括号与名字之间那个空格。
/// 这一族的真实症状是"两个时钟下拉里同一个时区长得不一样"（收口前两份逐字抄本各漂一次格式），
/// 不报错也不崩，所以要逐形状钉。
///
/// 钉不住的那一支如实记下：<b>"偏移要按当下那一瞬间取"没钉</b>——
/// 把 <c>GetUtcOffset(now)</c> 换成 <c>timeZone.BaseUtcOffset</c>，在没有夏令时区间的自定义时区上
/// 不会红（实测 7 格全绿）。要钉它得造一个带 <c>AdjustmentRule</c> 的时区、再取夏/冬两个瞬间，
/// 那是下一只手该补的夹具，不是这里已经覆盖的。反过来，形状钉得住：去掉小时的两位补零红 6 格。
/// </summary>
public sealed class TimeZoneDisplayLabelTests
{
    private static readonly DateTime AnyInstant = new(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(5, 30, "Kathmandu", "(UTC+05:30) Kathmandu")]
    [InlineData(-8, 0, "WestOfEight", "(UTC-08:00) WestOfEight")]
    [InlineData(0, 0, "Greenwich", "(UTC+00:00) Greenwich")]
    [InlineData(12, 45, "ChathamIslands", "(UTC+12:45) ChathamIslands")]
    [InlineData(-3, 30, "StJohns", "(UTC-03:30) StJohns")]
    public void Format_BuildsThePublishedShape(int hours, int minutes, string name, string expected)
    {
        var zone = Custom(hours, minutes, name);

        Assert.Equal(expected, TimeZoneDisplayLabel.Format(zone, AnyInstant));
    }

    [Fact]
    public void Format_UsesTheStandardName_AndKeepsOneSpaceAfterTheParenthesis()
    {
        var zone = Custom(1, 0, "Mid European");

        var label = TimeZoneDisplayLabel.Format(zone, AnyInstant);

        Assert.StartsWith("(UTC+01:00) ", label, StringComparison.Ordinal);
        Assert.EndsWith("Mid European", label, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_WithoutAnInstant_StillProducesThePublishedShape()
    {
        // 两个编辑器的真实用法：不传瞬间。这里只判形状，名字取系统自己的写法。
        Assert.Matches(@"^\(UTC[+-]\d{2}:\d{2}\) .+$", TimeZoneDisplayLabel.Format(TimeZoneInfo.Utc));
    }

    private static TimeZoneInfo Custom(int hours, int minutes, string name)
    {
        var offset = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(hours < 0 ? -minutes : minutes);
        return TimeZoneInfo.CreateCustomTimeZone(name, offset, name, name);
    }
}
