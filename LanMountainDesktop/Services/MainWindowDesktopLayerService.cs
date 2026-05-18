using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace LanMountainDesktop.Services;

public interface IMainWindowDesktopLayerService
{
    bool IsSupported { get; }
    void EnableOrRefresh(Window window);
    void Disable(Window window);
}

public static class MainWindowDesktopLayerServiceFactory
{
    private static readonly object Gate = new();
    private static IMainWindowDesktopLayerService? _instance;

    public static IMainWindowDesktopLayerService GetOrCreate()
    {
        lock (Gate)
        {
            return _instance ??= OperatingSystem.IsWindows()
                ? new WindowsMainWindowDesktopLayerService()
                : new NullMainWindowDesktopLayerService();
        }
    }
}

internal sealed class WindowsMainWindowDesktopLayerService : IMainWindowDesktopLayerService
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private const long WS_CHILD = 0x40000000L;
    private const long WS_POPUP = 0x80000000L;
    private const long WS_CAPTION = 0x00C00000L;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_MINIMIZEBOX = 0x00020000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;
    private const long WS_SYSMENU = 0x00080000L;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    private readonly object _gate = new();
    private readonly Dictionary<IntPtr, WindowRestoreState> _restoreStates = [];

    public bool IsSupported => true;

    public void EnableOrRefresh(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var handle = GetWindowHandle(window);
        if (handle == IntPtr.Zero)
        {
            window.Opened -= OnDeferredOpened;
            window.Opened += OnDeferredOpened;
            return;
        }

        EnableOrRefresh(handle);
    }

    public void Disable(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Opened -= OnDeferredOpened;

        var handle = GetWindowHandle(window);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        WindowRestoreState? restoreState;
        lock (_gate)
        {
            if (!_restoreStates.Remove(handle, out restoreState))
            {
                return;
            }
        }

        try
        {
            _ = SetParent(handle, restoreState.Parent);
            SetWindowLongPtr(handle, GWL_STYLE, restoreState.Style);
            SetWindowLongPtr(handle, GWL_EXSTYLE, restoreState.ExStyle);
            _ = SetWindowPos(
                handle,
                HWND_TOP,
                0,
                0,
                0,
                0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            AppLogger.Info("MainWindowDesktopLayer", $"Disabled desktop layer. Window={handle}.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("MainWindowDesktopLayer", $"Failed to disable desktop layer. Window={handle}.", ex);
        }
    }

    private void OnDeferredOpened(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Opened -= OnDeferredOpened;
        EnableOrRefresh(window);
    }

    private void EnableOrRefresh(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle))
        {
            return;
        }

        SaveRestoreStateIfNeeded(handle);
        var desktopHost = ResolveDesktopIconHost();
        if (desktopHost != IntPtr.Zero && IsWindow(desktopHost))
        {
            ApplyDesktopChildStyle(handle);
            if (GetParent(handle) != desktopHost)
            {
                _ = SetParent(handle, desktopHost);
            }

            _ = SetWindowPos(
                handle,
                HWND_TOP,
                0,
                0,
                0,
                0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            AppLogger.Info("MainWindowDesktopLayer", $"Enabled desktop layer. Window={handle}; Host={desktopHost}.");
            return;
        }

        _ = SetWindowPos(handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        AppLogger.Warn("MainWindowDesktopLayer", $"Desktop icon host not found. Falling back to HWND_BOTTOM. Window={handle}.");
    }

    private void SaveRestoreStateIfNeeded(IntPtr handle)
    {
        lock (_gate)
        {
            if (_restoreStates.ContainsKey(handle))
            {
                return;
            }

            _restoreStates[handle] = new WindowRestoreState(
                GetParent(handle),
                GetWindowLongPtr(handle, GWL_STYLE),
                GetWindowLongPtr(handle, GWL_EXSTYLE));
        }
    }

    private static void ApplyDesktopChildStyle(IntPtr handle)
    {
        var style = GetWindowLongPtr(handle, GWL_STYLE).ToInt64();
        style |= WS_CHILD;
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        SetWindowLongPtr(handle, GWL_STYLE, new IntPtr(style));
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
            var worker = FindWindowEx(topLevelWindow, IntPtr.Zero, "WorkerW", null);
            if (worker == IntPtr.Zero)
            {
                continue;
            }

            var defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                return defView;
            }
        }

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

    private sealed record WindowRestoreState(IntPtr Parent, IntPtr Style, IntPtr ExStyle);

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

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
}

internal sealed class NullMainWindowDesktopLayerService : IMainWindowDesktopLayerService
{
    public bool IsSupported => false;

    public void EnableOrRefresh(Window window)
    {
        AppLogger.Info("MainWindowDesktopLayer", "Desktop layer requested on an unsupported platform.");
    }

    public void Disable(Window window)
    {
    }
}
