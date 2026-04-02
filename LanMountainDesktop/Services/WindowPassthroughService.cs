using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace LanMountainDesktop.Services;

/// <summary>
/// 窗口置底服务接口
/// </summary>
public interface IWindowBottomMostService
{
    void SetupBottomMost(Window window);
    void SendToBottom(Window window);
    bool IsBottomMostSupported { get; }
}

/// <summary>
/// 区域级穿透服务接口 - 使用 WM_NCHITTEST 实现
/// </summary>
public interface IRegionPassthroughService
{
    /// <summary>
    /// 设置窗口的可交互区域
    /// </summary>
    void SetInteractiveRegions(Window window, IReadOnlyList<Rect> interactiveRegions);
    
    /// <summary>
    /// 清除所有可交互区域
    /// </summary>
    void ClearInteractiveRegions(Window window);
    
    /// <summary>
    /// 获取当前平台是否支持区域级穿透
    /// </summary>
    bool IsRegionPassthroughSupported { get; }
}

/// <summary>
/// 窗口置底服务工厂
/// </summary>
public static class WindowBottomMostServiceFactory
{
    private static IWindowBottomMostService? _instance;
    private static readonly object _lock = new();
    
    public static IWindowBottomMostService GetOrCreate()
    {
        lock (_lock)
        {
            return _instance ??= OperatingSystem.IsWindows()
                ? new WindowsWindowBottomMostService()
                : new NullWindowBottomMostService();
        }
    }
}

/// <summary>
/// 区域级穿透服务工厂
/// </summary>
public static class RegionPassthroughServiceFactory
{
    private static IRegionPassthroughService? _instance;
    private static readonly object _lock = new();
    
    public static IRegionPassthroughService GetOrCreate()
    {
        lock (_lock)
        {
            return _instance ??= OperatingSystem.IsWindows()
                ? new WindowsRegionPassthroughService()
                : new NullRegionPassthroughService();
        }
    }
}

/// <summary>
/// Windows 平台窗口置底服务
/// </summary>
internal sealed class WindowsWindowBottomMostService : IWindowBottomMostService
{
    private const int GWL_EXSTYLE = -20;
    private const int GWL_HWNDPARENT = -8;
    private const int GWLP_WNDPROC = -4;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int HTCLIENT = 1;
    
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private static readonly Dictionary<IntPtr, bool> _bottomMostWindows = new();
    private static readonly Dictionary<IntPtr, IntPtr> _originalWndProcs = new();
    private static readonly Dictionary<IntPtr, List<Rect>> _interactiveRegions = new();
    private static readonly object _staticLock = new();
    
    public bool IsBottomMostSupported => true;
    
    public void SetupBottomMost(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        
        window.Opened += (s, e) =>
        {
            var handle = GetWindowHandle(window);
            if (handle == IntPtr.Zero) return;
            
            // 设置扩展样式
            var exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            exStyle = (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED) & ~WS_EX_APPWINDOW;
            SetWindowLong(handle, GWL_EXSTYLE, exStyle);
            
            // 设置为桌面子窗口
            SetAsDesktopChild(handle);
            
            // 注册置底状态
            lock (_staticLock)
            {
                _bottomMostWindows[handle] = true;
                _interactiveRegions[handle] = [];
            }
            
            // 注入消息钩子
            InstallMessageHook(handle);
            
            // 初始置底
            SendToBottomInternal(handle);
            
            AppLogger.Info("WindowBottomMost", $"Window setup as bottom-most: {handle}");
        };
        
        window.Closed += (s, e) =>
        {
            var handle = GetWindowHandle(window);
            if (handle != IntPtr.Zero)
            {
                lock (_staticLock)
                {
                    _bottomMostWindows.Remove(handle);
                    _originalWndProcs.Remove(handle);
                    _interactiveRegions.Remove(handle);
                }
            }
        };
    }
    
    public void SendToBottom(Window window)
    {
        var handle = GetWindowHandle(window);
        if (handle != IntPtr.Zero) SendToBottomInternal(handle);
    }
    
    private static IntPtr GetWindowHandle(Window window)
    {
        try { return window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero; }
        catch { return IntPtr.Zero; }
    }
    
    private static void SendToBottomInternal(IntPtr handle)
    {
        SetWindowPos(handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }
    
    private static void SetAsDesktopChild(IntPtr handle)
    {
        var windowHandles = new ArrayList();
        EnumWindows(EnumWindowsCallback, windowHandles);
        foreach (IntPtr h in windowHandles)
        {
            var hDefView = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (hDefView != IntPtr.Zero)
            {
                SetWindowLong(handle, GWL_HWNDPARENT, hDefView.ToInt32());
                break;
            }
        }
    }
    
    private static bool EnumWindowsCallback(IntPtr handle, ArrayList handles)
    {
        handles.Add(handle);
        return true;
    }
    
    private static void InstallMessageHook(IntPtr handle)
    {
        var originalWndProc = GetWindowLongPtr(handle, GWLP_WNDPROC);
        if (originalWndProc == IntPtr.Zero) return;
        
        lock (_staticLock)
        {
            _originalWndProcs[handle] = originalWndProc;
        }
        
        SetWindowLongPtr(handle, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate<WndProcDelegate>(SubclassWndProc));
    }
    
    private static IntPtr SubclassWndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        // 处理 WM_WINDOWPOSCHANGING - 保持置底
        if (msg == WM_WINDOWPOSCHANGING)
        {
            lock (_staticLock)
            {
                if (_bottomMostWindows.TryGetValue(hWnd, out var isBottomMost) && isBottomMost)
                {
                    SendToBottomInternal(hWnd);
                }
            }
        }
        
        // 处理 WM_NCHITTEST - 区域级穿透
        if (msg == WM_NCHITTEST)
        {
            // 从 lParam 解析坐标（低字为 X，高字为 Y）
            var x = (short)(wParam.ToInt32() & 0xFFFF);
            var y = (short)((wParam.ToInt32() >> 16) & 0xFFFF);
            var point = new Point(x, y);
            
            lock (_staticLock)
            {
                if (_interactiveRegions.TryGetValue(hWnd, out var regions))
                {
                    foreach (var region in regions)
                    {
                        if (region.Contains(point))
                        {
                            // 在可交互区域内，返回 HTCLIENT
                            return (IntPtr)HTCLIENT;
                        }
                    }
                }
            }
            
            // 不在可交互区域内，返回 HTTRANSPARENT 让事件穿透
            return (IntPtr)HTTRANSPARENT;
        }
        
        // 调用原始窗口过程
        IntPtr originalWndProc;
        lock (_staticLock)
        {
            if (!_originalWndProcs.TryGetValue(hWnd, out originalWndProc))
            {
                return DefWindowProc(hWnd, msg, wParam, lParam);
            }
        }
        
        return CallWindowProc(originalWndProc, hWnd, msg, wParam, lParam);
    }
    
    /// <summary>
    /// 设置窗口的可交互区域（供 WindowsRegionPassthroughService 调用）
    /// </summary>
    internal static void SetInteractiveRegionsInternal(IntPtr handle, List<Rect> regions)
    {
        lock (_staticLock)
        {
            _interactiveRegions[handle] = regions;
        }
    }
    
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hParent, IntPtr hChildAfter, string? lpszClass, string? lpszWindow);
    
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, ArrayList lParam);
    
    private delegate bool EnumWindowsProc(IntPtr handle, ArrayList handles);
    
    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int uMsg, IntPtr wParam, IntPtr lParam);
}

/// <summary>
/// Windows 平台区域级穿透服务 - 使用 WM_NCHITTEST
/// </summary>
internal sealed class WindowsRegionPassthroughService : IRegionPassthroughService
{
    public bool IsRegionPassthroughSupported => true;
    
    public void SetInteractiveRegions(Window window, IReadOnlyList<Rect> interactiveRegions)
    {
        var handle = GetWindowHandle(window);
        if (handle == IntPtr.Zero) return;
        
        WindowsWindowBottomMostService.SetInteractiveRegionsInternal(handle, new List<Rect>(interactiveRegions));
        AppLogger.Info("RegionPassthrough", $"Set {interactiveRegions.Count} interactive regions.");
    }
    
    public void ClearInteractiveRegions(Window window)
    {
        var handle = GetWindowHandle(window);
        if (handle == IntPtr.Zero) return;
        
        WindowsWindowBottomMostService.SetInteractiveRegionsInternal(handle, []);
    }
    
    private static IntPtr GetWindowHandle(Window window)
    {
        try { return window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero; }
        catch { return IntPtr.Zero; }
    }
}

/// <summary>
/// 空实现
/// </summary>
internal sealed class NullWindowBottomMostService : IWindowBottomMostService
{
    public bool IsBottomMostSupported => false;
    public void SetupBottomMost(Window window) { }
    public void SendToBottom(Window window) { }
}

internal sealed class NullRegionPassthroughService : IRegionPassthroughService
{
    public bool IsRegionPassthroughSupported => false;
    public void SetInteractiveRegions(Window window, IReadOnlyList<Rect> interactiveRegions) { }
    public void ClearInteractiveRegions(Window window) { }
}
