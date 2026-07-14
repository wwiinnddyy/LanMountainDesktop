using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace LanMountainDesktop.Services;

public interface IWindowBottomMostService
{
    void SetupBottomMost(Window window);
    void SendToBottom(Window window);
    PixelPoint GetScreenPosition(Window window);
    bool SetScreenPosition(Window window, PixelPoint position, bool queueOnFailure = false);
    bool IsBottomMostSupported { get; }
}

public readonly record struct WindowInteractiveRegion(
    Rect Bounds,
    double CornerRadius,
    Matrix? ClientToRegionTransform = null,
    Rect? ClientClipBounds = null,
    double ClientClipCornerRadius = 0d);

public interface IRegionPassthroughService
{
    void SetInteractiveRegions(Window window, IReadOnlyList<WindowInteractiveRegion> interactiveRegions);
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
    private const int GWLP_HWNDPARENT = -8;

    private const uint WS_CHILD = 0x40000000U;
    private const uint WS_POPUP = 0x80000000U;
    private const uint WS_CAPTION = 0x00C00000U;
    private const uint WS_THICKFRAME = 0x00040000U;
    private const uint WS_MINIMIZEBOX = 0x00020000U;
    private const uint WS_MAXIMIZEBOX = 0x00010000U;
    private const uint WS_SYSMENU = 0x00080000U;

    private const uint WS_EX_TOOLWINDOW = 0x00000080U;
    private const uint WS_EX_APPWINDOW = 0x00040000U;
    private const uint WS_EX_NOACTIVATE = 0x08000000U;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000U;
    private const uint AVALONIA_COMPOSITION_EXSTYLE_MASK = WS_EX_NOREDIRECTIONBITMAP;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;

    private const uint WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int HTCLIENT = 1;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const uint DWMWCP_DONOTROUND = 1;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFEU;

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private static readonly object StaticLock = new();
    private static readonly object TimerLock = new();

    private static readonly Dictionary<Window, DesktopWindowState> WindowStates = new();

    private static System.Timers.Timer? _desktopHostMonitorTimer;
    private static IntPtr _lastResolvedDesktopHost;
    private static int _monitorDispatchPending;

    public bool IsBottomMostSupported => true;

    public void SetupBottomMost(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DesktopWindowState state;
        lock (StaticLock)
        {
            if (WindowStates.TryGetValue(window, out state!))
            {
                return;
            }

            state = new DesktopWindowState(window);
            WindowStates[window] = state;
        }

        Win32Properties.SetWindowCornerPreference(window, Win32Properties.WindowCornerPreference.DoNotRound);
        Win32Properties.AddWindowStylesCallback(window, state.WindowStylesCallback);
        Win32Properties.AddWndProcHookCallback(window, state.WndProcHookCallback);

        window.Closed += OnWindowClosed;

        var handle = GetWindowHandle(window);
        if (handle == IntPtr.Zero)
        {
            window.Opened += OnWindowOpened;
            return;
        }

        RunOnUiThread(() => InitializeAndAttach(state, handle, logSuccess: true));
    }

    public void SendToBottom(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!TryGetWindowState(window, out var state))
        {
            SetupBottomMost(window);
            return;
        }

        RunOnUiThread(() =>
        {
            var handle = GetWindowHandle(window);
            if (handle == IntPtr.Zero || !IsWindow(handle))
            {
                return;
            }

            RegisterHandle(state, handle);
            if (state.NeedsNativeRepair &&
                !ShouldAttemptNativeRepair(
                    false,
                    DateTime.UtcNow,
                    state.NextNativeRepairAttemptUtc))
            {
                return;
            }

            var desktopHost = ResolveDesktopIconHost();
            if (!state.NeedsNativeRepair &&
                state.IsDesktopAttached &&
                state.HasStableDesktopAttachment &&
                desktopHost != IntPtr.Zero &&
                state.DesktopHost == desktopHost &&
                GetParent(handle) == desktopHost &&
                state.OriginalState is { } originalState &&
                HasExpectedDesktopRoleStyles(handle, originalState))
            {
                _ = SetWindowPos(
                    handle,
                    HWND_TOP,
                    0,
                    0,
                    0,
                    0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                return;
            }

            ApplyDesktopAttachment(
                state,
                desktopHost,
                logSuccess: false,
                "explicit refresh",
                allowRetryFailedHost: true);
        });
    }

    public PixelPoint GetScreenPosition(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = GetWindowHandle(window);
        return handle != IntPtr.Zero && GetWindowRect(handle, out var rect)
            ? new PixelPoint(rect.Left, rect.Top)
            : window.Position;
    }

    public bool SetScreenPosition(
        Window window,
        PixelPoint position,
        bool queueOnFailure = false)
    {
        ArgumentNullException.ThrowIfNull(window);
        TryGetWindowState(window, out var state);
        var handle = GetWindowHandle(window);
        if (handle == IntPtr.Zero || !IsWindow(handle))
        {
            window.Position = position;
            if (state is not null)
            {
                state.PendingScreenPosition = null;
                state.HasLoggedPositionFailure = false;
            }

            return true;
        }

        var nativePosition = new POINT(position.X, position.Y);
        var style = ReadWindowStyle(handle, GWL_STYLE);
        var nativeParent = GetParent(handle);
        if (state is not null)
        {
            if (state.NeedsNativeRepair)
            {
                if (queueOnFailure)
                {
                    state.PendingScreenPosition = position;
                }

                return false;
            }

            if (state.IsDesktopAttached)
            {
                if (state.OriginalState is not { } originalState ||
                    nativeParent != state.DesktopHost ||
                    !HasExpectedDesktopRoleStyles(handle, originalState))
                {
                    LogPositionFailureOnce(
                        state,
                        $"Refusing to move a desktop window with invalid native attachment state. " +
                        $"Window={handle}; Parent={nativeParent}; ExpectedHost={state.DesktopHost}.");
                    if (queueOnFailure)
                    {
                        state.PendingScreenPosition = position;
                    }

                    return false;
                }

                nativeParent = state.DesktopHost;
            }
        }

        if (OriginalWindowUsesParentClientCoordinates(style) &&
            (nativeParent == IntPtr.Zero || !ScreenToClient(nativeParent, ref nativePosition)))
        {
            if (state is not null)
            {
                LogPositionFailureOnce(
                    state,
                    $"Could not translate screen position to child-window coordinates. " +
                    $"Window={handle}; Parent={nativeParent}.");
                if (queueOnFailure)
                {
                    state.PendingScreenPosition = position;
                }
            }
            return false;
        }

        if (!SetWindowPos(
                handle,
                IntPtr.Zero,
                nativePosition.X,
                nativePosition.Y,
                0,
                0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE))
        {
            if (state is not null)
            {
                LogPositionFailureOnce(
                    state,
                    $"Could not set screen position. Window={handle}; Position={position}; " +
                    $"Error={Marshal.GetLastWin32Error()}.");
                if (queueOnFailure)
                {
                    state.PendingScreenPosition = position;
                }
            }
            return false;
        }

        if (state is not null)
        {
            state.PendingScreenPosition = null;
            state.HasLoggedPositionFailure = false;
        }

        return true;
    }

    private static void LogPositionFailureOnce(DesktopWindowState state, string message)
    {
        if (state.HasLoggedPositionFailure)
        {
            return;
        }

        AppLogger.Warn("WindowBottomMost", message);
        state.HasLoggedPositionFailure = true;
    }

    private static void TryApplyPendingScreenPosition(DesktopWindowState state)
    {
        if (state.NeedsNativeRepair || state.PendingScreenPosition is not { } pendingPosition)
        {
            return;
        }

        _ = new WindowsWindowBottomMostService().SetScreenPosition(
            state.Window,
            pendingPosition,
            queueOnFailure: true);
    }

    internal static void SetInteractiveRegionsInternal(
        Window window,
        IReadOnlyList<WindowInteractiveRegion> regions)
    {
        if (!TryGetWindowState(window, out var state))
        {
            return;
        }

        var snapshot = new WindowInteractiveRegion[regions.Count];
        for (var i = 0; i < regions.Count; i++)
        {
            snapshot[i] = regions[i];
        }

        state.InteractiveRegions = snapshot;
    }

    internal static (uint Style, uint ExStyle) CreateDesktopChildStyles(uint style, uint exStyle)
    {
        style |= WS_CHILD;
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        return (style, ApplyDesktopRoleExtendedStyles(exStyle));
    }

    internal static bool IsPointInsideRegion(WindowInteractiveRegion region, Point point)
    {
        if (region.ClientClipBounds is { } clientClipBounds &&
            !IsPointInsideRoundedBounds(clientClipBounds, region.ClientClipCornerRadius, point))
        {
            return false;
        }

        if (region.ClientToRegionTransform is { } clientToRegionTransform)
        {
            point = clientToRegionTransform.Transform(point);
        }

        return IsPointInsideRoundedBounds(region.Bounds, region.CornerRadius, point);
    }

    private static bool IsPointInsideRoundedBounds(Rect bounds, double cornerRadius, Point point)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.Contains(point))
        {
            return false;
        }

        var radius = Math.Clamp(cornerRadius, 0, Math.Min(bounds.Width, bounds.Height) / 2);
        if (radius <= 0)
        {
            return true;
        }

        var localX = point.X - bounds.X;
        var localY = point.Y - bounds.Y;
        if (localX >= radius && localX <= bounds.Width - radius ||
            localY >= radius && localY <= bounds.Height - radius)
        {
            return true;
        }

        var centerX = localX < radius ? radius : bounds.Width - radius;
        var centerY = localY < radius ? radius : bounds.Height - radius;
        var deltaX = localX - centerX;
        var deltaY = localY - centerY;
        return deltaX * deltaX + deltaY * deltaY <= radius * radius;
    }

    internal static bool OriginalWindowUsesParentClientCoordinates(uint style)
    {
        return (style & WS_CHILD) != 0;
    }

    internal static bool ShouldAttemptNativeRepair(
        bool hostChanged,
        DateTime utcNow,
        DateTime nextRepairAttemptUtc)
    {
        return hostChanged || utcNow >= nextRepairAttemptUtc;
    }

    internal static bool ShouldAttemptDesktopAttachment(
        bool hostChanged,
        IntPtr attachedHost,
        IntPtr currentHost,
        bool parentMismatch)
    {
        return parentMismatch || (hostChanged && attachedHost != currentHost);
    }

    private static uint ApplyDesktopRoleExtendedStyles(uint exStyle)
    {
        // Preserve every compositor-managed bit from Avalonia. In particular, this method must
        // never opt the window into a second, legacy alpha-composition path.
        return (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
    }

    private static void OnWindowOpened(object? sender, EventArgs e)
    {
        if (sender is not Window window || !TryGetWindowState(window, out var state))
        {
            return;
        }

        window.Opened -= OnWindowOpened;
        var handle = GetWindowHandle(window);
        if (handle != IntPtr.Zero)
        {
            RunOnUiThread(() => InitializeAndAttach(state, handle, logSuccess: true));
        }
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window && TryGetWindowState(window, out var state))
        {
            CleanupWindow(state, restoreNativeState: true);
        }
    }

    private static void InitializeAndAttach(DesktopWindowState state, IntPtr handle, bool logSuccess)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle))
        {
            return;
        }

        RegisterHandle(state, handle);
        ConfigureDwmAppearance(handle);
        ApplyDesktopAttachment(state, ResolveDesktopIconHost(), logSuccess, "initial setup");
    }

    private static void RegisterHandle(DesktopWindowState state, IntPtr handle)
    {
        lock (StaticLock)
        {
            if (state.Handle != IntPtr.Zero && state.Handle != handle)
            {
                state.OriginalState = null;
                state.DesktopHost = IntPtr.Zero;
                state.IsDesktopAttached = false;
                state.HasStableDesktopAttachment = false;
                ResetNativeRepairState(state);
                state.AttachToCurrentHostAfterRepair = false;
                state.HasLoggedFallback = false;
                state.HasLoggedPositionFailure = false;
            }

            state.Handle = handle;
            state.OriginalState ??= new NativeWindowState(
                GetParent(handle),
                ReadWindowStyle(handle, GWL_STYLE),
                ReadWindowStyle(handle, GWL_EXSTYLE));
        }
    }

    private static void ApplyDesktopAttachment(
        DesktopWindowState state,
        IntPtr desktopHost,
        bool logSuccess,
        string reason,
        bool allowRetryFailedHost = false)
    {
        var handle = state.Handle;
        if (handle == IntPtr.Zero || !IsWindow(handle) || state.OriginalState is not { } originalState)
        {
            return;
        }

        var screenPosition = GetNativeScreenPosition(handle, state.Window.Position);
        if (state.NeedsNativeRepair)
        {
            var failedHost = state.FailedDesktopHost;
            var attachAfterRepair = state.AttachToCurrentHostAfterRepair;
            FallBackToBottom(
                state,
                screenPosition,
                "repairing an incomplete native rollback before attachment",
                logSuccess: false,
                failedHost);
            if (state.NeedsNativeRepair)
            {
                StartDesktopHostMonitorTimer(desktopHost);
                return;
            }

            state.AttachToCurrentHostAfterRepair = false;
            if (!attachAfterRepair && desktopHost == failedHost && !allowRetryFailedHost)
            {
                // The failed host has not changed. Stay in the safe top-level fallback until
                // Explorer changes or the caller explicitly requests another attachment.
                StartDesktopHostMonitorTimer(desktopHost);
                return;
            }
        }

        var beforeStyle = ReadWindowStyle(handle, GWL_STYLE);
        var beforeExStyle = ReadWindowStyle(handle, GWL_EXSTYLE);

        if (desktopHost == IntPtr.Zero || !IsWindow(desktopHost))
        {
            FallBackToBottom(
                state,
                screenPosition,
                "desktop icon host is unavailable",
                logSuccess,
                desktopHost);
            StartDesktopHostMonitorTimer(desktopHost);
            return;
        }

        if (state.IsDesktopAttached)
        {
            if (state.DesktopHost == desktopHost && GetParent(handle) == desktopHost)
            {
                if (HasExpectedDesktopRoleStyles(handle, originalState))
                {
                    StartDesktopHostMonitorTimer(desktopHost);
                    return;
                }

                FallBackToBottom(
                    state,
                    screenPosition,
                    "desktop child style validation failed",
                    logSuccess,
                    desktopHost);
                StartDesktopHostMonitorTimer(desktopHost);
                return;
            }

            var hostBeingDetached = state.DesktopHost;
            if (!TryRestoreNativeState(state, screenPosition, showWindow: true))
            {
                FallBackToBottom(
                    state,
                    screenPosition,
                    "failed to restore before remount",
                    logSuccess,
                    hostBeingDetached);
                if (state.NeedsNativeRepair && desktopHost != hostBeingDetached)
                {
                    state.AttachToCurrentHostAfterRepair = true;
                }

                if (state.NeedsNativeRepair ||
                    (desktopHost == hostBeingDetached && !allowRetryFailedHost))
                {
                    StartDesktopHostMonitorTimer(desktopHost);
                    return;
                }
            }

            beforeStyle = ReadWindowStyle(handle, GWL_STYLE);
            beforeExStyle = ReadWindowStyle(handle, GWL_EXSTYLE);
        }

        beforeExStyle |= originalState.ExStyle & AVALONIA_COMPOSITION_EXSTYLE_MASK;
        var (expectedStyle, expectedExStyle) = CreateDesktopChildStyles(beforeStyle, beforeExStyle);
        WriteWindowStyle(handle, GWL_STYLE, expectedStyle);
        WriteWindowStyle(handle, GWL_EXSTYLE, expectedExStyle);

        _ = SetParent(handle, desktopHost);
        var setParentError = Marshal.GetLastWin32Error();
        if (GetParent(handle) != desktopHost)
        {
            FallBackToBottom(
                state,
                screenPosition,
                $"SetParent failed with error {setParentError}",
                logSuccess,
                desktopHost);
            StartDesktopHostMonitorTimer(desktopHost);
            return;
        }

        state.DesktopHost = desktopHost;
        state.IsDesktopAttached = true;
        state.HasStableDesktopAttachment = false;

        var childPosition = new POINT(screenPosition.X, screenPosition.Y);
        if (!ScreenToClient(desktopHost, ref childPosition) ||
            !SetWindowPos(
                handle,
                HWND_TOP,
                childPosition.X,
                childPosition.Y,
                0,
                0,
                SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW) ||
            !HasExpectedDesktopStyles(handle, expectedStyle, expectedExStyle) ||
            GetParent(handle) != desktopHost)
        {
            var error = Marshal.GetLastWin32Error();
            FallBackToBottom(
                state,
                screenPosition,
                $"post-attachment validation failed with error {error}",
                logSuccess,
                desktopHost);
            StartDesktopHostMonitorTimer(desktopHost);
            return;
        }

        ResetNativeRepairState(state);
        state.AttachToCurrentHostAfterRepair = false;
        state.HasLoggedFallback = false;
        state.HasStableDesktopAttachment = true;
        ConfigureDwmAppearance(handle);
        if (logSuccess)
        {
            var afterStyle = ReadWindowStyle(handle, GWL_STYLE);
            var afterExStyle = ReadWindowStyle(handle, GWL_EXSTYLE);
            AppLogger.Info(
                "WindowBottomMost",
                $"Mounted window to desktop icon host. Window={handle}; Host={desktopHost}; Reason={reason}; " +
                $"Style=0x{beforeStyle:X8}->0x{afterStyle:X8}; ExStyle=0x{beforeExStyle:X8}->0x{afterExStyle:X8}; " +
                $"NoRedirectionBitmap={((afterExStyle & WS_EX_NOREDIRECTIONBITMAP) != 0)}.");
        }

        StartDesktopHostMonitorTimer(desktopHost);
    }

    private static void FallBackToBottom(
        DesktopWindowState state,
        PixelPoint screenPosition,
        string reason,
        bool logSuccess,
        IntPtr failedDesktopHost)
    {
        var handle = state.Handle;
        var wasAttached = state.IsDesktopAttached;
        var wasStablyAttached = state.HasStableDesktopAttachment;
        var restored = true;
        if (state.OriginalState is { } originalState &&
            (state.NeedsNativeRepair ||
             wasAttached ||
             GetParent(handle) != originalState.Parent ||
             ReadWindowStyle(handle, GWL_STYLE) != originalState.Style ||
             ReadWindowStyle(handle, GWL_EXSTYLE) != originalState.ExStyle))
        {
            restored = TryRestoreNativeState(state, screenPosition, showWindow: true);
        }

        if (!restored)
        {
            MarkNativeRepairPending(state, failedDesktopHost);
            if (logSuccess || !state.HasLoggedRepairFailure)
            {
                AppLogger.Warn(
                    "WindowBottomMost",
                    $"Native rollback is incomplete; keeping the window in repair state. " +
                    $"Window={handle}; FailedHost={failedDesktopHost}; Reason={reason}.");
                state.HasLoggedRepairFailure = true;
            }

            return;
        }

        state.DesktopHost = IntPtr.Zero;
        state.IsDesktopAttached = false;
        state.HasStableDesktopAttachment = false;

        if (IsWindow(handle) &&
            !SetWindowPos(
                handle,
                HWND_BOTTOM,
                0,
                0,
                0,
                0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW))
        {
            MarkNativeRepairPending(state, failedDesktopHost);
            if (logSuccess || !state.HasLoggedRepairFailure)
            {
                AppLogger.Warn(
                    "WindowBottomMost",
                    $"Native state was restored, but HWND_BOTTOM fallback positioning failed. " +
                    $"Window={handle}; FailedHost={failedDesktopHost}; Reason={reason}; " +
                    $"Error={Marshal.GetLastWin32Error()}.");
                state.HasLoggedRepairFailure = true;
            }

            return;
        }

        ResetNativeRepairState(state);

        if (logSuccess || wasStablyAttached || !state.HasLoggedFallback)
        {
            AppLogger.Warn(
                "WindowBottomMost",
                $"Using HWND_BOTTOM fallback. Window={handle}; Reason={reason}; NativeStateRestored={restored}.");
            state.HasLoggedFallback = true;
        }
    }

    private static void MarkNativeRepairPending(DesktopWindowState state, IntPtr failedDesktopHost)
    {
        state.FailedDesktopHost = failedDesktopHost;
        state.NativeRepairAttemptCount = Math.Min(state.NativeRepairAttemptCount + 1, 30);
        var exponent = Math.Min(state.NativeRepairAttemptCount - 1, 5);
        var delaySeconds = Math.Min(60, 2 * (1 << exponent));
        state.NextNativeRepairAttemptUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
        state.NeedsNativeRepair = true;

        if (state.Handle != IntPtr.Zero && IsWindow(state.Handle))
        {
            _ = SetWindowPos(
                state.Handle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
        }
    }

    private static void ResetNativeRepairState(DesktopWindowState state)
    {
        state.NeedsNativeRepair = false;
        state.FailedDesktopHost = IntPtr.Zero;
        state.HasLoggedRepairFailure = false;
        state.NativeRepairAttemptCount = 0;
        state.NextNativeRepairAttemptUtc = DateTime.MinValue;
    }

    private static bool TryRestoreNativeState(
        DesktopWindowState state,
        PixelPoint screenPosition,
        bool showWindow)
    {
        var handle = state.Handle;
        if (handle == IntPtr.Zero || !IsWindow(handle) || state.OriginalState is not { } originalState)
        {
            return false;
        }

        var previousDesktopHost = state.DesktopHost;
        var wasDesktopAttached = state.IsDesktopAttached;
        var wasStablyDesktopAttached = state.HasStableDesktopAttachment;
        state.IsDesktopAttached = false;
        state.HasStableDesktopAttachment = false;
        state.DesktopHost = IntPtr.Zero;

        var restoreParentOrOwner = originalState.Parent != IntPtr.Zero && !IsWindow(originalState.Parent)
            ? IntPtr.Zero
            : originalState.Parent;
        var restorePosition = new POINT(screenPosition.X, screenPosition.Y);

        if (OriginalWindowUsesParentClientCoordinates(originalState.Style))
        {
            _ = SetParent(handle, restoreParentOrOwner);
            WriteWindowStyle(handle, GWL_STYLE, originalState.Style);
            WriteWindowStyle(handle, GWL_EXSTYLE, originalState.ExStyle);
            if (restoreParentOrOwner != IntPtr.Zero &&
                !ScreenToClient(restoreParentOrOwner, ref restorePosition))
            {
                restorePosition = new POINT(screenPosition.X, screenPosition.Y);
            }
        }
        else
        {
            // GetParent returns an owner for a top-level popup. It is not a child-coordinate
            // parent: first leave the desktop child hierarchy, restore the popup styles, then
            // restore the owner through GWLP_HWNDPARENT and keep SetWindowPos in screen pixels.
            _ = SetParent(handle, IntPtr.Zero);
            WriteWindowStyle(handle, GWL_STYLE, originalState.Style);
            WriteWindowStyle(handle, GWL_EXSTYLE, originalState.ExStyle);
            _ = SetWindowLongPtr(handle, GWLP_HWNDPARENT, restoreParentOrOwner);
        }

        var flags = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED;
        if (showWindow)
        {
            flags |= SWP_SHOWWINDOW;
        }

        var positioned = SetWindowPos(
            handle,
            IntPtr.Zero,
            restorePosition.X,
            restorePosition.Y,
            0,
            0,
            flags);

        var hierarchyAndStylesRestored =
            GetParent(handle) == restoreParentOrOwner &&
            ReadWindowStyle(handle, GWL_STYLE) == originalState.Style &&
            ReadWindowStyle(handle, GWL_EXSTYLE) == originalState.ExStyle;
        if (!hierarchyAndStylesRestored)
        {
            // Preserve the logical attachment state until a later repair attempt succeeds.
            // This prevents the monitor from treating a half-converted child HWND as a valid
            // top-level fallback.
            state.DesktopHost = previousDesktopHost;
            state.IsDesktopAttached = wasDesktopAttached;
            state.HasStableDesktopAttachment = wasStablyDesktopAttached;
        }

        return hierarchyAndStylesRestored && positioned;
    }

    private static bool HasExpectedDesktopStyles(IntPtr handle, uint style, uint exStyle)
    {
        return ReadWindowStyle(handle, GWL_STYLE) == style &&
               ReadWindowStyle(handle, GWL_EXSTYLE) == exStyle;
    }

    private static bool HasExpectedDesktopRoleStyles(IntPtr handle, NativeWindowState originalState)
    {
        var style = ReadWindowStyle(handle, GWL_STYLE);
        var exStyle = ReadWindowStyle(handle, GWL_EXSTYLE);
        var expected = CreateDesktopChildStyles(style, exStyle);
        var requiredAvaloniaBits = originalState.ExStyle & AVALONIA_COMPOSITION_EXSTYLE_MASK;
        return style == expected.Style &&
               exStyle == expected.ExStyle &&
               (exStyle & requiredAvaloniaBits) == requiredAvaloniaBits;
    }

    private static void ConfigureDwmAppearance(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle))
        {
            return;
        }

        try
        {
            var cornerPreference = DWMWCP_DONOTROUND;
            _ = DwmSetWindowAttribute(
                handle,
                DWMWA_WINDOW_CORNER_PREFERENCE,
                ref cornerPreference,
                sizeof(uint));

            var borderColor = DWMWA_COLOR_NONE;
            _ = DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // These attributes are best-effort and unavailable on older Windows versions.
        }
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

    private static void StartDesktopHostMonitorTimer(IntPtr currentHost)
    {
        lock (TimerLock)
        {
            if (_desktopHostMonitorTimer != null)
            {
                return;
            }

            _lastResolvedDesktopHost = currentHost;
            _desktopHostMonitorTimer = new System.Timers.Timer(TimeSpan.FromSeconds(2))
            {
                AutoReset = true
            };
            _desktopHostMonitorTimer.Elapsed += (_, _) => MonitorDesktopHostAttachments();
            _desktopHostMonitorTimer.Start();
        }
    }

    private static void MonitorDesktopHostAttachments()
    {
        var desktopHost = ResolveDesktopIconHost();
        var hostChanged = false;
        lock (TimerLock)
        {
            if (desktopHost != _lastResolvedDesktopHost)
            {
                _lastResolvedDesktopHost = desktopHost;
                hostChanged = true;
            }
        }

        List<DesktopWindowState> states;
        lock (StaticLock)
        {
            states = [.. WindowStates.Values];
        }

        var requiresCleanupOrRepair = false;
        var now = DateTime.UtcNow;
        foreach (var state in states)
        {
            if ((state.NeedsNativeRepair && now >= state.NextNativeRepairAttemptUtc) ||
                (!state.NeedsNativeRepair && state.PendingScreenPosition.HasValue) ||
                state.Handle != IntPtr.Zero && !IsWindow(state.Handle) ||
                state.IsDesktopAttached &&
                (GetParent(state.Handle) != state.DesktopHost ||
                 state.OriginalState is not { } originalState ||
                 !HasExpectedDesktopRoleStyles(state.Handle, originalState)))
            {
                requiresCleanupOrRepair = true;
                break;
            }
        }

        if (!hostChanged && !requiresCleanupOrRepair ||
            Interlocked.Exchange(ref _monitorDispatchPending, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var currentHost = ResolveDesktopIconHost();
                var effectiveHostChanged = hostChanged || currentHost != desktopHost;
                List<DesktopWindowState> currentStates;
                lock (StaticLock)
                {
                    currentStates = [.. WindowStates.Values];
                }

                foreach (var state in currentStates)
                {
                    if (state.Handle == IntPtr.Zero)
                    {
                        // A window can be registered before Avalonia creates its native handle.
                        continue;
                    }

                    if (!IsWindow(state.Handle))
                    {
                        CleanupWindow(state, restoreNativeState: false);
                        continue;
                    }

                    if (state.NeedsNativeRepair)
                    {
                        if (!ShouldAttemptNativeRepair(
                                effectiveHostChanged,
                                DateTime.UtcNow,
                                state.NextNativeRepairAttemptUtc))
                        {
                            continue;
                        }

                        var failedHost = state.FailedDesktopHost;
                        var attachAfterRepair = state.AttachToCurrentHostAfterRepair;
                        var screenPosition = GetNativeScreenPosition(state.Handle, state.Window.Position);
                        FallBackToBottom(
                            state,
                            screenPosition,
                            "retrying incomplete native rollback",
                            logSuccess: false,
                            failedHost);
                        if (state.NeedsNativeRepair)
                        {
                            continue;
                        }

                        state.AttachToCurrentHostAfterRepair = false;
                        if (currentHost != IntPtr.Zero &&
                            (attachAfterRepair ||
                             effectiveHostChanged && currentHost != failedHost))
                        {
                            ApplyDesktopAttachment(
                                state,
                                currentHost,
                                logSuccess: true,
                                "desktop host changed while native state was being repaired");
                        }

                        TryApplyPendingScreenPosition(state);
                        continue;
                    }

                    var attachmentDrift = state.IsDesktopAttached &&
                                          (GetParent(state.Handle) != state.DesktopHost ||
                                           state.OriginalState is not { } originalState ||
                                           !HasExpectedDesktopRoleStyles(state.Handle, originalState));
                    if (ShouldAttemptDesktopAttachment(
                            effectiveHostChanged,
                            state.DesktopHost,
                            currentHost,
                            attachmentDrift))
                    {
                        ApplyDesktopAttachment(state, currentHost, logSuccess: true, "desktop host changed");
                    }

                    TryApplyPendingScreenPosition(state);
                }

                lock (TimerLock)
                {
                    _lastResolvedDesktopHost = currentHost;
                }
            }
            finally
            {
                Interlocked.Exchange(ref _monitorDispatchPending, 0);
            }
        }, DispatcherPriority.Background);
    }

    private static void CleanupWindow(DesktopWindowState state, bool restoreNativeState)
    {
        state.Window.Opened -= OnWindowOpened;
        state.Window.Closed -= OnWindowClosed;
        Win32Properties.RemoveWindowStylesCallback(state.Window, state.WindowStylesCallback);
        Win32Properties.RemoveWndProcHookCallback(state.Window, state.WndProcHookCallback);

        if (restoreNativeState && state.Handle != IntPtr.Zero && IsWindow(state.Handle))
        {
            var screenPosition = GetNativeScreenPosition(state.Handle, state.Window.Position);
            _ = TryRestoreNativeState(state, screenPosition, showWindow: false);
        }

        lock (StaticLock)
        {
            WindowStates.Remove(state.Window);
        }

        StopDesktopHostMonitorTimerIfIdle();
    }

    private static void StopDesktopHostMonitorTimerIfIdle()
    {
        lock (StaticLock)
        {
            if (WindowStates.Count > 0)
            {
                return;
            }
        }

        lock (TimerLock)
        {
            _desktopHostMonitorTimer?.Stop();
            _desktopHostMonitorTimer?.Dispose();
            _desktopHostMonitorTimer = null;
            _lastResolvedDesktopHost = IntPtr.Zero;
        }
    }

    private static IntPtr HandleWindowMessage(
        DesktopWindowState state,
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WM_NCHITTEST)
        {
            return IntPtr.Zero;
        }

        if (state.NeedsNativeRepair)
        {
            handled = true;
            return (IntPtr)HTTRANSPARENT;
        }

        var screenPoint = new POINT(
            unchecked((short)(lParam.ToInt64() & 0xFFFF)),
            unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF)));
        if (!ScreenToClient(hWnd, ref screenPoint))
        {
            handled = true;
            return (IntPtr)HTTRANSPARENT;
        }

        var point = ConvertPhysicalClientPointToDip(
            new Point(screenPoint.X, screenPoint.Y),
            GetWindowDpiScale(hWnd));
        var regions = state.InteractiveRegions;
        foreach (var region in regions)
        {
            if (IsPointInsideRegion(region, point))
            {
                handled = true;
                return (IntPtr)HTCLIENT;
            }
        }

        handled = true;
        return (IntPtr)HTTRANSPARENT;
    }

    internal static Point ConvertPhysicalClientPointToDip(Point physicalPoint, double dpiScale)
    {
        var scale = double.IsFinite(dpiScale) ? Math.Max(0.1, dpiScale) : 1d;
        return new Point(physicalPoint.X / scale, physicalPoint.Y / scale);
    }

    private static double GetWindowDpiScale(IntPtr handle)
    {
        try
        {
            var dpi = GetDpiForWindow(handle);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 1.0;
        }
    }

    private static PixelPoint GetNativeScreenPosition(IntPtr handle, PixelPoint fallback)
    {
        return GetWindowRect(handle, out var rect)
            ? new PixelPoint(rect.Left, rect.Top)
            : fallback;
    }

    private static bool TryGetWindowState(Window window, out DesktopWindowState state)
    {
        lock (StaticLock)
        {
            return WindowStates.TryGetValue(window, out state!);
        }
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private static uint ReadWindowStyle(IntPtr handle, int index)
    {
        return unchecked((uint)GetWindowLongPtr(handle, index).ToInt64());
    }

    private static void WriteWindowStyle(IntPtr handle, int index, uint value)
    {
        _ = SetWindowLongPtr(handle, index, new IntPtr(unchecked((int)value)));
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    private sealed class DesktopWindowState
    {
        public DesktopWindowState(Window window)
        {
            Window = window;
            WindowStylesCallback = ApplyWindowStyles;
            WndProcHookCallback = ProcessWindowMessage;
        }

        public Window Window { get; }
        public IntPtr Handle { get; set; }
        public NativeWindowState? OriginalState { get; set; }
        public IntPtr DesktopHost { get; set; }
        public volatile bool IsDesktopAttached;
        public volatile bool HasStableDesktopAttachment;
        public volatile bool NeedsNativeRepair;
        public IntPtr FailedDesktopHost { get; set; }
        public bool AttachToCurrentHostAfterRepair { get; set; }
        public int NativeRepairAttemptCount { get; set; }
        public DateTime NextNativeRepairAttemptUtc { get; set; }
        public PixelPoint? PendingScreenPosition { get; set; }
        public bool HasLoggedPositionFailure { get; set; }
        public bool HasLoggedFallback { get; set; }
        public bool HasLoggedRepairFailure { get; set; }
        public volatile WindowInteractiveRegion[] InteractiveRegions = [];
        public Win32Properties.CustomWindowStylesCallback WindowStylesCallback { get; }
        public Win32Properties.CustomWndProcHookCallback WndProcHookCallback { get; }

        private (uint style, uint exStyle) ApplyWindowStyles(uint style, uint exStyle)
        {
            return IsDesktopAttached
                ? CreateDesktopChildStyles(style, exStyle)
                : (style, ApplyDesktopRoleExtendedStyles(exStyle));
        }

        private IntPtr ProcessWindowMessage(
            IntPtr hWnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            return HandleWindowMessage(this, hWnd, message, wParam, lParam, ref handled);
        }
    }

    private readonly record struct NativeWindowState(IntPtr Parent, uint Style, uint ExStyle);

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(
        IntPtr hParent,
        IntPtr hChildAfter,
        string? lpszClass,
        string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hWnd,
        int dwAttribute,
        ref uint pvAttribute,
        int cbAttribute);
}

internal sealed class WindowsRegionPassthroughService : IRegionPassthroughService
{
    public bool IsRegionPassthroughSupported => true;

    public void SetInteractiveRegions(
        Window window,
        IReadOnlyList<WindowInteractiveRegion> interactiveRegions)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(interactiveRegions);
        WindowsWindowBottomMostService.SetInteractiveRegionsInternal(window, interactiveRegions);
    }

    public void ClearInteractiveRegions(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        WindowsWindowBottomMostService.SetInteractiveRegionsInternal(window, []);
    }
}

internal sealed class NullWindowBottomMostService : IWindowBottomMostService
{
    public bool IsBottomMostSupported => false;
    public void SetupBottomMost(Window window) { }
    public void SendToBottom(Window window) { }
    public PixelPoint GetScreenPosition(Window window) => window.Position;
    public bool SetScreenPosition(
        Window window,
        PixelPoint position,
        bool queueOnFailure = false)
    {
        window.Position = position;
        return true;
    }
}

internal sealed class NullRegionPassthroughService : IRegionPassthroughService
{
    public bool IsRegionPassthroughSupported => false;
    public void SetInteractiveRegions(Window window, IReadOnlyList<WindowInteractiveRegion> interactiveRegions) { }
    public void ClearInteractiveRegions(Window window) { }
}
