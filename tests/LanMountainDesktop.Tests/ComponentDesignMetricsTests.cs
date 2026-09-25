using Avalonia;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件"按格子缩放"两条算式的家唯一的证据。收口前四个热搜/新闻组件各抄一份 12 行
/// （两对逐字相同，只差上端那一档），模拟时钟与计时器又各抄一份 6 行的表盘算式。
///
/// 值得钉的两件事都不报错：① 期望尺寸里有两处除法，格子大小或设计格数为 0 时不许算出 0 缩放
/// （"组件缩成一团/看不清"正是 <c>ApplyCellSize</c> 钳最小 1 格的同一个理由）；
/// ② 布局还没落定（Bounds 是 0）时按"等于期望尺寸"算，也就是先按 1 画、下一次尺寸变化再修正。
/// 上限（2.8 / 2.4）与设计格数（2×4 / 4×4）是各组件自己的视觉口径，故意留给调用方，
/// 所以这里只钉"调用方给多少就夹到多少"，不钉它们该是多少。
/// </summary>
public sealed class ComponentDesignMetricsTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void FootprintScale_TakesTheShorterSide()
    {
        var scale = ComponentDesignMetrics.ResolveFootprintScale(
            cellSize: 100, new Rect(0, 0, 400, 100), baseWidthCells: 4, baseHeightCells: 2, maxScale: 2.8);

        // 宽正好等于期望（400 = 100×4）→ 1；高只有一半 → 0.5，取短的那一边再夹到下限 0.72。
        Assert.Equal(0.72, scale, Tolerance);
    }

    [Fact]
    public void FootprintScale_ClampsAtTheCeilingTheCallerSupplies()
    {
        var wide = ComponentDesignMetrics.ResolveFootprintScale(
            cellSize: 100, new Rect(0, 0, 2000, 2000), baseWidthCells: 4, baseHeightCells: 2, maxScale: 2.8);
        var tight = ComponentDesignMetrics.ResolveFootprintScale(
            cellSize: 100, new Rect(0, 0, 2000, 2000), baseWidthCells: 4, baseHeightCells: 2, maxScale: 2.4);

        Assert.Equal(2.8, wide, Tolerance);
        Assert.Equal(2.4, tight, Tolerance);
    }

    [Fact]
    public void FootprintScale_BeforeTheLayoutLands_DrawsAtOne()
    {
        var scale = ComponentDesignMetrics.ResolveFootprintScale(
            cellSize: 100, new Rect(0, 0, 0, 0), baseWidthCells: 4, baseHeightCells: 2, maxScale: 2.8);

        Assert.Equal(1d, scale, Tolerance);
    }

    [Fact]
    public void FootprintScale_InvalidGridOrFootprint_ReturnsOneInsteadOfDividingByZero()
    {
        Assert.Equal(1d, ComponentDesignMetrics.ResolveFootprintScale(
            cellSize: 0, new Rect(0, 0, 400, 200), baseWidthCells: 4, baseHeightCells: 2, maxScale: 2.8), Tolerance);
        Assert.Equal(1d, ComponentDesignMetrics.ResolveFootprintScale(
            cellSize: 100, new Rect(0, 0, 400, 200), baseWidthCells: 0, baseHeightCells: 2, maxScale: 2.8), Tolerance);
    }

    [Fact]
    public void DialScale_MultipliesTheBoundsTierButNeverPastTheCellTier()
    {
        // 格子 48 对 44 的参照 → 1.0909…；300×300 两边都是 1.0，×1.05 = 1.05 才是较小的那一个。
        var scale = ComponentDesignMetrics.ResolveDialScale(48, new Rect(0, 0, 300, 300));

        Assert.Equal(1.05, scale, Tolerance);
    }

    [Fact]
    public void DialScale_UsesItsOwnReferenceNotTheDesignBaseCellSize()
    {
        // 钉现状：44 是表盘算式的参照，不等于 ComponentDesignMetrics.BaseCellSize（48）。
        // 把 44 顺手改成 48 会同时改变两块时钟的字号与指针长度——那是视觉决定，不是收口。
        Assert.Equal(1d, ComponentDesignMetrics.ResolveDialScale(44, new Rect(0, 0, 0, 0)), Tolerance);
        Assert.NotEqual(1d, ComponentDesignMetrics.ResolveDialScale(ComponentDesignMetrics.BaseCellSize, new Rect(0, 0, 0, 0)), Tolerance);
    }

    [Fact]
    public void DialScale_ClampsBothEnds()
    {
        Assert.Equal(1.90, ComponentDesignMetrics.ResolveDialScale(200, new Rect(0, 0, 3000, 3000)), Tolerance);
        Assert.Equal(0.60, ComponentDesignMetrics.ResolveDialScale(1, new Rect(0, 0, 10, 10)), Tolerance);
    }
}
