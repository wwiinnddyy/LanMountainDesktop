using Avalonia;
using Avalonia.Controls;

namespace LanMountainDesktop.Platform.Abstractions;

/// <summary>
/// 窗口置底服务接口。使组件窗口保持在桌面层（Z 序最底）。
/// </summary>
public interface IWindowBottomMostService
{
    void SetupBottomMost(Window window);
    void SendToBottom(Window window);
    PixelPoint GetScreenPosition(Window window);
    bool SetScreenPosition(Window window, PixelPoint position, bool queueOnFailure = false);
    bool IsBottomMostSupported { get; }
}

/// <summary>
/// 窗口交互区域定义。区域外的点击穿透到桌面。
/// </summary>
public readonly record struct WindowInteractiveRegion(
    Rect Bounds,
    double CornerRadius,
    Matrix? ClientToRegionTransform = null,
    Rect? ClientClipBounds = null,
    double ClientClipCornerRadius = 0d);

/// <summary>
/// 区域点击穿透服务接口。
/// </summary>
public interface IRegionPassthroughService
{
    void SetInteractiveRegions(Window window, IReadOnlyList<WindowInteractiveRegion> interactiveRegions);
    void ClearInteractiveRegions(Window window);
    bool IsRegionPassthroughSupported { get; }
}

/// <summary>
/// 无操作实现：非 Windows 平台窗口置底不可用。
/// </summary>
public sealed class NullWindowBottomMostService : IWindowBottomMostService
{
    public bool IsBottomMostSupported => false;

    public void SetupBottomMost(Window window) { }

    public void SendToBottom(Window window) { }

    public PixelPoint GetScreenPosition(Window window) => window.Position;

    public bool SetScreenPosition(Window window, PixelPoint position, bool queueOnFailure = false)
    {
        window.Position = position;
        return true;
    }
}

/// <summary>
/// 无操作实现：非 Windows 平台区域穿透不可用。
/// </summary>
public sealed class NullRegionPassthroughService : IRegionPassthroughService
{
    public bool IsRegionPassthroughSupported => false;

    public void SetInteractiveRegions(Window window, IReadOnlyList<WindowInteractiveRegion> interactiveRegions) { }

    public void ClearInteractiveRegions(Window window) { }
}
