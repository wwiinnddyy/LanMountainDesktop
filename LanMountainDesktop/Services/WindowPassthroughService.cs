using System;
using LanMountainDesktop.Platform.Abstractions;
using LanMountainDesktop.Platform.Windows;

namespace LanMountainDesktop.Services;

/// <summary>
/// 窗口置底/区域穿透服务工厂。接口与实现位于平台层：
/// 接口（IWindowBottomMostService / IRegionPassthroughService / WindowInteractiveRegion）
/// 在 Platform.Abstractions，Windows 实现（P/Invoke）在 Platform.Windows。
/// </summary>
public static class WindowBottomMostServiceFactory
{
    private static IWindowBottomMostService? _instance;
    private static readonly object _lock = new();

    public static IWindowBottomMostService GetOrCreate()
    {
        lock (_lock)
        {
            if (_instance is null)
            {
                PlatformLogBridge.Install();
                _instance = OperatingSystem.IsWindows()
                    ? new WindowsWindowBottomMostService()
                    : new NullWindowBottomMostService();
            }

            return _instance;
        }
    }
}

public static class RegionPassthroughServiceFactory
{
    private static IRegionPassthroughService? _instance;
    private static readonly object _lock = new();

    public static IRegionPassthroughService GetOrCreate()
    {
        lock (_lock)
        {
            if (_instance is null)
            {
                PlatformLogBridge.Install();
                _instance = OperatingSystem.IsWindows()
                    ? new WindowsRegionPassthroughService()
                    : new NullRegionPassthroughService();
            }

            return _instance;
        }
    }
}
