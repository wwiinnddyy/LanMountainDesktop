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
    private const int WM_ACTIVATEAPP = 0x001C;  // 【新增】应用激活消息
    private const int HTTRANSPARENT = -1;
    private const int HTCLIENT = 1;
    
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private static readonly Dictionary<IntPtr, bool> _bottomMostWindows = new();
    private static readonly Dictionary<IntPtr, IntPtr> _originalWndProcs = new();
    private static readonly Dictionary<IntPtr, List<Rect>> _interactiveRegions = new();
    
    // 记录每个窗口的屏幕原点（窗口左上角的屏幕坐标），用于将 WM_NCHITTEST 屏幕坐标转成窗口相对坐标
    private static readonly Dictionary<IntPtr, Point> _windowScreenOrigins = new();
    private static readonly object _staticLock = new();
    
    // 【修复问题1】静态持有委托引用，防止 GC 回收导致 CallbackOnCollectedDelegate 崩溃
    private static WndProcDelegate? _wndProcDelegate;
    
    // 【修复问题2】记录每个窗口的 DPI 缩放比例
    private static readonly Dictionary<IntPtr, double> _windowDpiScales = new();
    
    // 【修复问题5】Z 轴竞争优化 - 记录上次置底时间，避免频繁操作
    private static readonly Dictionary<IntPtr, long> _lastSendToBottomTime = new();
    private const long MinSendToBottomIntervalMs = 100;  // 【修复置底问题】降低到 100ms，提高响应速度
    
    // 【新增】定时器定期强制置底
    private static System.Timers.Timer? _keepBottomTimer;
    private static readonly object _timerLock = new();
    
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
            
            // 注册置底状态 & 记录窗口屏幕原点
            lock (_staticLock)
            {
                _bottomMostWindows[handle] = true;
                _interactiveRegions[handle] = [];
                UpdateWindowScreenOrigin(handle);
                UpdateWindowDpiScale(handle);  // 【修复问题2】初始化 DPI 缩放
            }
            
            // 注入消息钩子
            InstallMessageHook(handle);
            
            // 初始置底
            SendToBottomInternal(handle);
            
            // 【新增】启动定时器定期强制置底
            StartKeepBottomTimer();
            
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
                    _windowScreenOrigins.Remove(handle);
                    _windowDpiScales.Remove(handle);  // 【修复问题2】清理 DPI 缩放记录
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
    
    /// <summary>
    /// 【新增】启动定时器定期强制置底所有窗口
    /// </summary>
    private static void StartKeepBottomTimer()
    {
        lock (_timerLock)
        {
            if (_keepBottomTimer != null) return;
            
            _keepBottomTimer = new System.Timers.Timer(200);  // 每 200ms 检查一次
            _keepBottomTimer.Elapsed += (s, e) =>
            {
                try
                {
                    lock (_staticLock)
                    {
                        foreach (var kvp in _bottomMostWindows)
                        {
                            if (kvp.Value)  // 如果标记为置底
                            {
                                SendToBottomInternal(kvp.Key);
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略定时器错误
                }
            };
            _keepBottomTimer.Start();
        }
    }
    
    /// <summary>
    /// 【新增】停止定时器
    /// </summary>
    private static void StopKeepBottomTimer()
    {
        lock (_timerLock)
        {
            _keepBottomTimer?.Stop();
            _keepBottomTimer?.Dispose();
            _keepBottomTimer = null;
        }
    }
    
    private static void SetAsDesktopChild(IntPtr handle)
    {
        // 【修复问题4】增强桌面挂载逻辑，支持 Wallpaper Engine 等动态壁纸软件
        
        // 方案1: 尝试找到 WorkerW 层（Wallpaper Engine 创建的层）
        var workerW = IntPtr.Zero;
        var hDefView = IntPtr.Zero;
        
        // 枚举所有顶层窗口
        var windowHandles = new ArrayList();
        EnumWindows(EnumWindowsCallback, windowHandles);
        
        foreach (IntPtr h in windowHandles)
        {
            // 查找 WorkerW 窗口（Wallpaper Engine 创建）
            var className = GetWindowClassName(h);
            if (className == "WorkerW")
            {
                // 在 WorkerW 下查找 SHELLDLL_DefView
                var defView = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                {
                    workerW = h;
                    hDefView = defView;
                    break;
                }
            }
        }
        
        // 如果找到了 WorkerW 层，使用它作为父窗口
        if (workerW != IntPtr.Zero && hDefView != IntPtr.Zero)
        {
            SetWindowLong(handle, GWL_HWNDPARENT, hDefView.ToInt32());
            AppLogger.Info("WindowBottomMost", "Mounted to WorkerW layer (Wallpaper Engine detected)");
            return;
        }
        
        // 方案2: 回退到传统方式，查找 Progman 下的 SHELLDLL_DefView
        foreach (IntPtr h in windowHandles)
        {
            hDefView = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (hDefView != IntPtr.Zero)
            {
                SetWindowLong(handle, GWL_HWNDPARENT, hDefView.ToInt32());
                AppLogger.Info("WindowBottomMost", "Mounted to traditional desktop layer");
                break;
            }
        }
    }
    
    /// <summary>
    /// 【修复问题4】获取窗口类名
    /// </summary>
    private static string GetWindowClassName(IntPtr hWnd)
    {
        var buffer = new char[256];
        var length = GetClassName(hWnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
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
            
            // 【修复问题1】确保委托实例被静态引用持有，防止 GC 回收
            _wndProcDelegate ??= SubclassWndProc;
        }
        
        SetWindowLongPtr(handle, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }
    
    private static IntPtr SubclassWndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        // 【新增】处理应用激活消息 - 当其他应用激活时立即置底
        if (msg == WM_ACTIVATEAPP)
        {
            lock (_staticLock)
            {
                if (_bottomMostWindows.TryGetValue(hWnd, out var isBottomMost) && isBottomMost)
                {
                    // 立即置底，不进行频率限制
                    SendToBottomInternal(hWnd);
                }
            }
        }
        
        // 处理 WM_WINDOWPOSCHANGING - 保持置底
        if (msg == WM_WINDOWPOSCHANGING)
        {
            lock (_staticLock)
            {
                if (_bottomMostWindows.TryGetValue(hWnd, out var isBottomMost) && isBottomMost)
                {
                    // 【修复问题5】优化 Z 轴竞争 - 限制置底操作频率
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (_lastSendToBottomTime.TryGetValue(hWnd, out var lastTime))
                    {
                        if (now - lastTime < MinSendToBottomIntervalMs)
                        {
                            // 跳过过于频繁的置底操作
                            goto CallOriginal;
                        }
                    }
                    
                    SendToBottomInternal(hWnd);
                    _lastSendToBottomTime[hWnd] = now;
                }
            }
        }
        
        // 处理 WM_NCHITTEST - 区域级穿透
        if (msg == WM_NCHITTEST)
        {
            // WM_NCHITTEST 的鼠标坐标在 lParam（低16位=X，高16位=Y），且为屏幕坐标
            var screenX = (short)(lParam.ToInt64() & 0xFFFF);
            var screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            
            lock (_staticLock)
            {
                if (_interactiveRegions.TryGetValue(hWnd, out var regions) && regions.Count > 0)
                {
                    // 【修复问题2】获取窗口原点和 DPI 缩放比例
                    _windowScreenOrigins.TryGetValue(hWnd, out var origin);
                    _windowDpiScales.TryGetValue(hWnd, out var dpiScale);
                    if (dpiScale <= 0) dpiScale = 1.0;  // 默认缩放为 1.0
                    
                    // 将屏幕物理像素坐标转为窗口相对坐标
                    var clientX = screenX - origin.X;
                    var clientY = screenY - origin.Y;
                    
                    // 【修复问题2】将物理像素坐标转换为逻辑 DIP 坐标
                    // _interactiveRegions 存储的是 Avalonia UI 的逻辑 DIP 坐标
                    var logicalX = clientX / dpiScale;
                    var logicalY = clientY / dpiScale;
                    var point = new Point(logicalX, logicalY);
                    
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
        CallOriginal:
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
            // 同步刷新屏幕原点（DPI 缩放可能影响坐标，每次更新区域时一并刷新）
            UpdateWindowScreenOrigin(handle);
            UpdateWindowDpiScale(handle);  // 【修复问题2】同步更新 DPI 缩放
        }
    }
    
    /// <summary>
    /// 更新指定窗口的屏幕左上角坐标缓存（用于将 WM_NCHITTEST 屏幕坐标转为窗口相对坐标）
    /// </summary>
    private static void UpdateWindowScreenOrigin(IntPtr handle)
    {
        if (GetWindowRect(handle, out var rect))
        {
            _windowScreenOrigins[handle] = new Point(rect.Left, rect.Top);
        }
    }
    
    /// <summary>
    /// 【修复问题2】更新指定窗口的 DPI 缩放比例
    /// </summary>
    private static void UpdateWindowDpiScale(IntPtr handle)
    {
        try
        {
            // 获取窗口所在的显示器 DPI
            var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out var _) == 0)
                {
                    // DPI 缩放比例 = 当前 DPI / 96 (标准 DPI)
                    _windowDpiScales[handle] = dpiX / 96.0;
                }
            }
        }
        catch
        {
            // 如果获取失败，使用默认缩放 1.0
            _windowDpiScales[handle] = 1.0;
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    
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
    
    // 【修复问题2】DPI 相关的 P/Invoke 声明
    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;
    
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, int dwFlags);
    
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    
    // 【修复问题4】获取窗口类名的 P/Invoke
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);
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
