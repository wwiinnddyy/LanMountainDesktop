using System;
using LanMountainDesktop.Platform.Abstractions;
using LanMountainDesktop.Platform.Windows;

namespace LanMountainDesktop.Services;

/// <summary>
/// 桌面层服务工厂。接口与实现位于平台层：
/// 接口 <see cref="IMainWindowDesktopLayerService"/> 在 Platform.Abstractions，
/// Windows 实现（P/Invoke）在 Platform.Windows。
/// </summary>
public static class MainWindowDesktopLayerServiceFactory
{
    private static readonly object Gate = new();
    private static IMainWindowDesktopLayerService? _instance;

    public static IMainWindowDesktopLayerService GetOrCreate()
    {
        lock (Gate)
        {
            if (_instance is null)
            {
                PlatformLogBridge.Install();
                _instance = OperatingSystem.IsWindows()
                    ? new WindowsMainWindowDesktopLayerService()
                    : new NullMainWindowDesktopLayerService();
            }

            return _instance;
        }
    }
}
