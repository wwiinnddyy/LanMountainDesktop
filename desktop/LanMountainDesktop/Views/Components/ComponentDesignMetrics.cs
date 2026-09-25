using Avalonia;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 桌面组件"按格子缩放"的基准尺寸：12 个组件各自写了 <c>48</c>，用它把当前格子边长换算成字号/内边距的缩放系数。
/// 这不是运行期的格子大小（那个随视口和密度算出来，见 <c>DesktopGridMetrics.CellSize</c>），
/// 而是"设计稿按 48 的格子画"这个约定。改动它会让所有组件同时改变缩放基线，所以只许有一处。
/// </summary>
internal static class ComponentDesignMetrics
{
    /// <summary>组件自缩放的设计基准格子边长（设备无关单位）。</summary>
    public const double BaseCellSize = 48d;

    /// <summary>
    /// 把宿主下发的格子边长落到组件自己的字段上，再触发一次重排。
    /// 钳到最少 1 格是刻意的：0 或负数会让组件按 0 尺寸排版（缩放算式里还有除法），
    /// 症状不是崩而是"组件缩成一团/看不清"，且没有任何异常提示。
    /// 18 个组件原先各写一份 <c>_currentCellSize = Math.Max(1, cellSize); 再重排</c>。
    /// </summary>
    public static void ApplyCellSize(ref double target, double cellSize, Action reflow)
    {
        target = Math.Max(1, cellSize);
        reflow();
    }

    /// <summary>
    /// "按设计占几格"的内容缩放：期望尺寸 = 格子边长 × 设计格数，实测尺寸与它相比取<b>短的那一边</b>，
    /// 再夹进 <c>[minScale, maxScale]</c>。格子或格数不合法（0 与负数）时给 <b>1 而不是 0</b>——
    /// 这条算式里有两处除法，给 0 会让整块面板缩成一团且不报错（同 <see cref="ApplyCellSize"/> 钳最小 1 格的理由）。
    /// 还没落下来尺寸时（<c>Bounds &lt;= 1</c>）按"正好等于期望尺寸"算，也就是先按 1 画、下一次 SizeChanged 再修正。
    /// 四个热搜/新闻组件此前各抄一份（<c>BaiduHotSearchWidget</c> 与 <c>BilibiliHotSearchWidget</c> 逐字相同、
    /// <c>IfengNewsWidget</c> 与 <c>JuyaNewsWidget</c> 逐字相同，只差上端那一档）。
    /// <b>格数与上限留给调用方</b>：2×4 与 4×4 是各自的设计占位、2.8 与 2.4 是各自的上端，
    /// 那些是视觉口径不是复制漂移，统一它们要先拍设计。
    /// </summary>
    public static double ResolveFootprintScale(
        double cellSize,
        Rect bounds,
        int baseWidthCells,
        int baseHeightCells,
        double maxScale,
        double minScale = 0.72)
    {
        var expectedWidth = cellSize * baseWidthCells;
        var expectedHeight = cellSize * baseHeightCells;
        if (expectedWidth <= 0 || expectedHeight <= 0)
        {
            return 1d;
        }

        var actualWidth = bounds.Width > 1 ? bounds.Width : expectedWidth;
        var actualHeight = bounds.Height > 1 ? bounds.Height : expectedHeight;
        return Math.Clamp(Math.Min(actualWidth / expectedWidth, actualHeight / expectedHeight), minScale, maxScale);
    }

    /// <summary>
    /// 圆形表盘（模拟时钟 / 计时器）那套缩放：格子档夹一次、宽高各夹一次，再取
    /// "格子档与两边实测档的较小值 × 1.05"，最后夹进 <c>[0.58, 1.95]</c>。
    /// 与 <see cref="ResolveFootprintScale"/> 是<b>两条不同算式</b>（表盘没有"设计占几格"这个参照），
    /// 两份抄本逐字相同所以收一处；<see cref="DialCellReference"/> 保持抄本里的 44 原样——
    /// 它<b>不等于</b> <see cref="BaseCellSize"/>（48），改成 48 会同时改变两块时钟的字号与指针长度，
    /// 那属视觉决定不是收口（未查证过这两个 44 是刻意还是历史遗留）。
    /// </summary>
    public static double ResolveDialScale(double cellSize, Rect bounds)
    {
        var cellScale = Math.Clamp(cellSize / DialCellReference, 0.60, 1.90);
        var heightScale = bounds.Height > 1 ? Math.Clamp(bounds.Height / 300d, 0.58, 2.0) : 1;
        var widthScale = bounds.Width > 1 ? Math.Clamp(bounds.Width / 300d, 0.58, 2.0) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(heightScale, widthScale) * 1.05), 0.58, 1.95);
    }

    /// <summary>表盘算式的格子参照，见 <see cref="ResolveDialScale"/> 的说明。</summary>
    public const double DialCellReference = 44d;
}
