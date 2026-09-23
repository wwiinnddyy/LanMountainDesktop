using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 星期表头这一家的行为钉：选哪张表、按列序填进去。
/// 收口前这两个组件各写一份逐字相同的 6 行循环，抄对了没地方能红；
/// 错法的症状是日历顶上一列对错了字（周日格显示成"一"），或者整行空白。
/// </summary>
public sealed class CalendarWeekLabelsTests
{
    private static IReadOnlyList<TextBlock> SevenBlocks()
    {
        var blocks = new List<TextBlock>(7);
        for (var i = 0; i < 7; i++)
        {
            blocks.Add(new TextBlock());
        }

        return blocks;
    }

    private static string Joined(IEnumerable<TextBlock> blocks) =>
        string.Join(',', blocks.Select(block => block.Text));

    [AvaloniaFact]
    public void ApplyHeaders_FillsTheChineseLabelsInColumnOrder()
    {
        var blocks = SevenBlocks();

        CalendarWeekLabels.ApplyHeaders(isChinese: true, blocks);

        Assert.Equal("日,一,二,三,四,五,六", Joined(blocks));
    }

    [AvaloniaFact]
    public void ApplyHeaders_FillsTheEnglishLabelsInColumnOrder()
    {
        var blocks = SevenBlocks();

        CalendarWeekLabels.ApplyHeaders(isChinese: false, blocks);

        // 英文这套就是 S M T W T F S：字母重复是原样，列序靠位置而不是靠唯一性。
        Assert.Equal("S,M,T,W,T,F,S", Joined(blocks));
    }

    [AvaloniaFact]
    public void ApplyHeaders_OnlyFillsTheBlocksTheControlGives()
    {
        var few = new List<TextBlock> { new(), new(), new() };

        CalendarWeekLabels.ApplyHeaders(isChinese: true, few);

        Assert.Equal("日,一,二", Joined(few));
    }

    [AvaloniaFact]
    public void ApplyHeaders_OnNoBlocks_WritesNothing()
    {
        var none = new List<TextBlock>();

        CalendarWeekLabels.ApplyHeaders(isChinese: false, none);

        Assert.Empty(none);
    }
}
