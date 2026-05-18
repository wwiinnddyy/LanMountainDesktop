using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace LanMountainDesktop.Services;

public interface IWindowBottomMostService
{
    void SetupBottomMost(Window window);
    void SendToBottom(Window window);
    bool IsBottomMostSupported { get; }
}

public interface IRegionPassthroughService
{
    void SetInteractiveRegions(Window window, IReadOnlyList<Rect> interactiveRegions);
    void ClearInteractiveRegions(Window window);
    bool IsRegionPassthroughSupported { get; }
}

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

internal sealed class WindowsWindowBottomMostService : IWindowBottomMostService
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int GWLP_WNDPROC = -4;

    private const long WS_CHILD = 0x40000000L;
    private const long WS_POPUP = 0x80000000L;
    private const long WS_CAPTION = 0x00C00000L;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_MINIMIZEBOX = 0x00020000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;
    private const long WS_SYSMENU = 0x00080000L;

    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;
    private const long WS_EX_LAYERED = 0x00080000L;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int HTCLIENT = 1;

    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private static readonly object _staticLock = new();
    private static readonly object _timerLock = new();

    private static readonly Dictionary<IntPtr, DesktopWindowState> _desktopWindows = new();
    private static readonly Dictionary<IntPtr, IntPtr> _originalWndProcs = new();
    private static readonly Dictionary<IntPtr, List<Rect>> _interactiveRegions = new();
    private static readonly Dictionary<IntPtr, Point> _windowScreenOrigins = new();
    private static readonly Dictionary<IntPtr, double> _windowDpiScales = new();

    private static WndProcDelegate? _wndProcDelegate;
    private static System.Timers.Timer? _desktopHostMonitorTimer;

    public bool IsBottomMostSupported => true;

    public void SetupBottomMost(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = GetWindowHandle(window);
        if (handle != IntPtr.Zero)
        {
            ApplyDesktopAttachment(handle, logSuccess: true);
        }
        else
        {
            window.Opened += (_, _) =>
            {
                var openedHandle = GetWindowHandle(window);
                if (openedHandle != IntPtr.Zero)
                {
                    ApplyDesktopAttachment(openedHandle, logSuccess: true);
                }
            };
        }

        window.Closed += (_, _) =>
        {
            var closedHandle = GetWindowHandle(window);
            if (closedHandle != IntPtr.Zero)
            {
                CleanupWindow(closedHandle);
            }
        };
    }

    public void SendToBottom(Window window)
    {
        var handle = GetWindowHandle(window);
        if (handle != IntPtr.Zero)
        {
            ApplyDesktopAttachment(handle, logSuccess: false);
        }
    }

    internal static void SetInteractiveRegionsInternal(IntPtr handle, List<Rect> regions)
    {
        lock (_staticLock)
        {
            _interactiveRegions[handle] = regions;
            UpdateWindowScreenOrigin(handle);
            UpdateWindowDpiScale(handle);
        }
    }

    private static void ApplyDesktopAttachment(IntPtr handle, bool logSuccess)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle))
        {
            return;
        }

        SetDesktopChildStyles(handle);
        InstallMessageHook(handle);

        var attached = TryAttachToDesktopIconHost(handle, out var desktopHost);
        lock (_staticLock)
        {
            _desktopWindows[handle] = new DesktopWindowState(desktopHost, attached);
            if (!_interactiveRegions.ContainsKey(handle))
            {
                _interactiveRegions[handle] = [];
            }

            UpdateWindowScreenOrigin(handle);
            UpdateWindowDpiScale(handle);
        }

        if (attached)
        {
            SetWindowPos(handle, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            if (logSuccess)
            {
                AppLogger.Info("WindowBottomMost", $"Mounted window to desktop icon host. Window={handle}; Host={desktopHost}");
            }
        }
        else
        {
            SetWindowPos(handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            if (logSuccess)
            {
                AppLogger.Warn("WindowBottomMost", $"Desktop icon host not found. Falling back to HWND_BOTTOM. Window={handle}");
            }
        }

        StartDesktopHostMonitorTimer();
    }

    private static void SetDesktopChildStyles(IntPtr handle)
    {
        var style = GetWindowLongPtr(handle, GWL_STYLE).ToInt64();
        style |= WS_CHILD;
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        SetWindowLongPtr(handle, GWL_STYLE, new IntPtr(style));

        var exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
        exStyle = (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private static bool TryAttachToDesktopIconHost(IntPtr handle, out IntPtr desktopHost)
    {
        desktopHost = ResolveDesktopIconHost();
        if (desktopHost == IntPtr.Zero || !IsWindow(desktopHost))
        {
            return false;
        }

        if (GetParent(handle) != desktopHost)
        {
            _ = SetParent(handle, desktopHost);
            if (GetParent(handle) != desktopHost)
            {
                return false;
            }
        }

        return true;
    }

    private static IntPtr ResolveDesktopIconHost()
    {
        var topLevelWindows = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            topLevelWindows.Add(handle);
            return true;
        }, IntPtr.Zero);

        foreach (var topLevelWindow in topLevelWindows)
        {
            var defView = FindWindowEx(topLevelWindow, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                return defView;
            }
        }

        return IntPtr.Zero;
    }

    private static void StartDesktopHostMonitorTimer()
    {
        lock (_timerLock)
        {
            if (_desktopHostMonitorTimer != null)
            {
                return;
            }

            _desktopHostMonitorTimer = new System.Timers.Timer(TimeSpan.FromSeconds(2));
            _desktopHostMonitorTimer.Elapsed += (_, _) => MonitorDesktopHostAttachments();
            _desktopHostMonitorTimer.Start();
        }
    }

    private static void MonitorDesktopHostAttachments()
    {
        List<IntPtr> handles;
        lock (_staticLock)
        {
            handles = [.. _desktopWindows.Keys];
        }

        foreach (var handle in handles)
        {
            if (!IsWindow(handle))
            {
                CleanupWindow(handle);
                continue;
            }

            ApplyDesktopAttachment(handle, logSuccess: false);
        }
    }

    private static void StopDesktopHostMonitorTimerIfIdle()
    {
        lock (_timerLock)
        {
            lock (_staticLock)
            {
                if (_desktopWindows.Count > 0)
                {
                    return;
                }
            }

            _desktopHostMonitorTimer?.Stop();
            _desktopHostMonitorTimer?.Dispose();
            _desktopHostMonitorTimer = null;
        }
    }

    private static void CleanupWindow(IntPtr handle)
    {
        IntPtr originalWndProc;
        lock (_staticLock)
        {
            if (_originalWndProcs.TryGetValue(handle, out originalWndProc) &&
                originalWndProc != IntPtr.Zero &&
                IsWindow(handle))
            {
                SetWindowLongPtr(handle, GWLP_WNDPROC, originalWndProc);
            }

            _desktopWindows.Remove(handle);
            _originalWndProcs.Remove(handle);
            _interactiveRegions.Remove(handle);
            _windowScreenOrigins.Remove(handle);
            _windowDpiScales.Remove(handle);
        }

        StopDesktopHostMonitorTimerIfIdle();
    }

    private static void InstallMessageHook(IntPtr handle)
    {
        lock (_staticLock)
        {
            if (_originalWndProcs.ContainsKey(handle))
            {
                return;
            }
        }

        var originalWndProc = GetWindowLongPtr(handle, GWLP_WNDPROC);
        if (originalWndProc == IntPtr.Zero)
        {
            return;
        }

        lock (_staticLock)
        {
            _originalWndProcs[handle] = originalWndProc;
            _wndProcDelegate ??= SubclassWndProc;
        }

        SetWindowLongPtr(handle, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }

    private static IntPtr SubclassWndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_NCHITTEST)
        {
            var screenX = (short)(lParam.ToInt64() & 0xFFFF);
            var screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

            lock (_staticLock)
            {
                if (_interactiveRegions.TryGetValue(hWnd, out var regions) && regions.Count > 0)
                {
                    _windowScreenOrigins.TryGetValue(hWnd, out var origin);
                    _windowDpiScales.TryGetValue(hWnd, out var dpiScale);
                    if (dpiScale <= 0)
                    {
                        dpiScale = 1.0;
                    }

                    var point = new Point((screenX - origin.X) / dpiScale, (screenY - origin.Y) / dpiScale);
                    foreach (var region in regions)
                    {
                        if (region.Contains(point))
                        {
                            return (IntPtr)HTCLIENT;
                        }
                    }
                }
            }

            return (IntPtr)HTTRANSPARENT;
        }

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

    private static void UpdateWindowScreenOrigin(IntPtr handle)
    {
        if (GetWindowRect(handle, out var rect))
        {
            _windowScreenOrigins[handle] = new Point(rect.Left, rect.Top);
        }
    }

    private static void UpdateWindowDpiScale(IntPtr handle)
    {
        try
        {
            var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero &&
                GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0)
            {
                _windowDpiScales[handle] = dpiX / 96.0;
                return;
            }
        }
        catch
        {
            // Use the default below.
        }

        _windowDpiScales[handle] = 1.0;
    }

    private static IntPtr GetWindowHandle(Window window)
    {
        try
        {
            return window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed record DesktopWindowState(IntPtr DesktopHost, bool IsDesktopAttached);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hParent, IntPtr hChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, int dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}

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
        try
        {
            return window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}

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
