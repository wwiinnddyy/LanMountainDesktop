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
}
