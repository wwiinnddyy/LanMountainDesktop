using Avalonia.Controls;

namespace LanMountainDesktop.Platform.Abstractions;

/// <summary>
/// 主窗口桌面层服务接口。将主窗口嵌入系统桌面图标层（仅 Windows 支持）。
/// </summary>
public interface IMainWindowDesktopLayerService
{
    bool IsSupported { get; }
    void EnableOrRefresh(Window window);
    void Disable(Window window);
}

/// <summary>
/// 无操作实现。用于不支持桌面层嵌入的平台（Linux/macOS/移动端）。
/// </summary>
public sealed class NullMainWindowDesktopLayerService : IMainWindowDesktopLayerService
{
    public bool IsSupported => false;

    public void EnableOrRefresh(Window window)
    {
        PlatformLog.Info("MainWindowDesktopLayer", "Desktop layer requested on an unsupported platform.");
    }

    public void Disable(Window window)
    {
    }
}
