using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Models;

/// <summary>
/// 融合桌面组件放置快照 - 用于在系统桌面（负一屏）上放置组件
/// </summary>
public sealed class FusedDesktopComponentPlacementSnapshot
{
    /// <summary>
    /// 放置实例ID（唯一标识）
    /// </summary>
    public string PlacementId { get; set; } = string.Empty;
    
    /// <summary>
    /// 组件类型ID
    /// </summary>
    public string ComponentId { get; set; } = string.Empty;
    
    /// <summary>
    /// X 坐标（像素，相对于屏幕左上角）
    /// </summary>
    public double X { get; set; }
    
    /// <summary>
    /// Y 坐标（像素，相对于屏幕左上角）
    /// </summary>
    public double Y { get; set; }
    
    /// <summary>
    /// 宽度（像素）
    /// </summary>
    public double Width { get; set; } = 200;
    
    /// <summary>
    /// 高度（像素）
    /// </summary>
    public double Height { get; set; } = 200;
    
    /// <summary>
    /// Z-Index（用于控制组件层叠顺序）
    /// </summary>
    public int ZIndex { get; set; }
    
    /// <summary>
    /// 是否锁定位置（锁定后不可拖动）
    /// </summary>
    public bool IsLocked { get; set; }
    
    /// <summary>
    /// 创建深拷贝
    /// </summary>
    public FusedDesktopComponentPlacementSnapshot Clone()
    {
        return new FusedDesktopComponentPlacementSnapshot
        {
            PlacementId = PlacementId,
            ComponentId = ComponentId,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            ZIndex = ZIndex,
            IsLocked = IsLocked
        };
    }
}

/// <summary>
/// 融合桌面布局快照 - 包含所有在系统桌面上显示的组件
/// </summary>
public sealed class FusedDesktopLayoutSnapshot
{
    /// <summary>
    /// 是否启用融合桌面功能
    /// </summary>
    public bool IsEnabled { get; set; }
    
    /// <summary>
    /// 组件放置列表
    /// </summary>
    public List<FusedDesktopComponentPlacementSnapshot> ComponentPlacements { get; set; } = [];
    
    /// <summary>
    /// 创建深拷贝
    /// </summary>
    public FusedDesktopLayoutSnapshot Clone()
    {
        return new FusedDesktopLayoutSnapshot
        {
            IsEnabled = IsEnabled,
            ComponentPlacements = [.. ComponentPlacements.ConvertAll(p => p.Clone())]
        };
    }
}
