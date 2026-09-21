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
}
