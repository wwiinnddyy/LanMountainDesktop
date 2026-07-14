using System.Reflection;
using Avalonia;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class WindowPassthroughServiceTests
{
    private const uint WsChild = 0x40000000U;
    private const uint WsPopup = 0x80000000U;
    private const uint WsCaption = 0x00C00000U;
    private const uint WsThickFrame = 0x00040000U;
    private const uint WsMinimizeBox = 0x00020000U;
    private const uint WsMaximizeBox = 0x00010000U;
    private const uint WsSysMenu = 0x00080000U;
    private const uint WsVisible = 0x10000000U;
    private const uint WsExToolWindow = 0x00000080U;
    private const uint WsExAppWindow = 0x00040000U;
    private const uint WsExNoActivate = 0x08000000U;
    private const uint WsExNoRedirectionBitmap = 0x00200000U;
    private const uint WsExTopmost = 0x00000008U;

    [Fact]
    public void DesktopChildStylePolicy_PreservesAvaloniaCompositionBits()
    {
        var style = WsPopup | WsCaption | WsThickFrame | WsMinimizeBox |
                    WsMaximizeBox | WsSysMenu | WsVisible;
        var exStyle = WsExNoRedirectionBitmap | WsExTopmost | WsExAppWindow;

        var result = Invoke<(uint Style, uint ExStyle)>(
            "CreateDesktopChildStyles",
            style,
            exStyle);

        Assert.NotEqual(0U, result.Style & WsChild);
        Assert.NotEqual(0U, result.Style & WsVisible);
        Assert.Equal(0U, result.Style & WsPopup);
        Assert.Equal(0U, result.Style & WsCaption);
        Assert.Equal(0U, result.Style & WsThickFrame);
        Assert.Equal(0U, result.Style & WsMinimizeBox);
        Assert.Equal(0U, result.Style & WsMaximizeBox);
        Assert.Equal(0U, result.Style & WsSysMenu);

        Assert.NotEqual(0U, result.ExStyle & WsExNoRedirectionBitmap);
        Assert.NotEqual(0U, result.ExStyle & WsExTopmost);
        Assert.NotEqual(0U, result.ExStyle & WsExToolWindow);
        Assert.NotEqual(0U, result.ExStyle & WsExNoActivate);
        Assert.Equal(0U, result.ExStyle & WsExAppWindow);
    }

    [Fact]
    public void NativeIntegration_UsesAvaloniaCallbacksWithoutManualWndProcSubclassing()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "Services", "WindowPassthroughService.cs");

        Assert.Contains("Win32Properties.AddWindowStylesCallback", source);
        Assert.Contains("Win32Properties.AddWndProcHookCallback", source);
        Assert.Contains("Win32Properties.RemoveWindowStylesCallback", source);
        Assert.Contains("Win32Properties.RemoveWndProcHookCallback", source);
        Assert.DoesNotContain("WS_EX_LAYERED", source);
        Assert.DoesNotContain("GWLP_WNDPROC", source);
        Assert.DoesNotContain("_originalWndProcs", source);
        Assert.Contains("SWP_FRAMECHANGED", source);
        Assert.Contains("DwmSetWindowAttribute", source);
        Assert.Contains("WindowCornerPreference.DoNotRound", source);
        Assert.Contains("NeedsNativeRepair", source);
        Assert.Contains("retrying incomplete native rollback", source);
        Assert.Contains("if (!restored)", source);
        Assert.Contains("SWP_HIDEWINDOW", source);
    }

    [Theory]
    [InlineData(96d, 200d, 120d, 200d, 120d)]
    [InlineData(120d, 250d, 150d, 200d, 120d)]
    [InlineData(144d, 300d, 180d, 200d, 120d)]
    [InlineData(192d, 400d, 240d, 200d, 120d)]
    public void HitTestCoordinates_ScalePhysicalPixelsAcrossSupportedDpiRanges(
        double dpi,
        double physicalX,
        double physicalY,
        double expectedX,
        double expectedY)
    {
        var result = Invoke<Point>(
            "ConvertPhysicalClientPointToDip",
            new Point(physicalX, physicalY),
            dpi / 96d);

        Assert.Equal(new Point(expectedX, expectedY), result);
    }

    [Fact]
    public void HitTesting_ExcludesRoundedTransparentCorners()
    {
        var region = new WindowInteractiveRegion(new Rect(0, 0, 100, 80), 20);

        Assert.False(IsInside(region, new Point(1, 1)));
        Assert.False(IsInside(region, new Point(99, 1)));
        Assert.False(IsInside(region, new Point(1, 79)));
        Assert.False(IsInside(region, new Point(99, 79)));
        Assert.True(IsInside(region, new Point(50, 1)));
        Assert.True(IsInside(region, new Point(1, 40)));
        Assert.True(IsInside(region, new Point(50, 40)));
    }

    [Fact]
    public void HitTesting_SupportsNegativeDesktopCoordinatesAndRectangularEditRegions()
    {
        var rounded = new WindowInteractiveRegion(new Rect(-100, -50, 100, 80), 20);
        var editRegion = new WindowInteractiveRegion(new Rect(-100, -50, 100, 80), 0);

        Assert.False(IsInside(rounded, new Point(-99, -49)));
        Assert.True(IsInside(rounded, new Point(-50, -49)));
        Assert.True(IsInside(rounded, new Point(-50, -10)));
        Assert.True(IsInside(editRegion, new Point(-99, -49)));
        Assert.False(IsInside(editRegion, new Point(1, -10)));
    }

    [Fact]
    public void HitTesting_UsesInverseRegionTransformForScaledAndTranslatedRoots()
    {
        var clientToLocal = new Matrix(
            0.5, 0,
            0, 0.5,
            -5, -10);
        var region = new WindowInteractiveRegion(
            new Rect(0, 0, 100, 80),
            20,
            clientToLocal);

        Assert.False(IsInside(region, new Point(12, 22)));
        Assert.True(IsInside(region, new Point(110, 100)));
        Assert.False(IsInside(region, new Point(212, 100)));
    }

    [Fact]
    public void HitTesting_UsesLocalRoundedGeometryForRotatedRoots()
    {
        var clientToLocal = new Matrix(
            0, -1,
            1, 0,
            -50, 200);
        var region = new WindowInteractiveRegion(
            new Rect(0, 0, 100, 80),
            20,
            clientToLocal);

        Assert.True(IsInside(region, new Point(160, 100)));
        Assert.False(IsInside(region, new Point(121, 51)));
        Assert.False(IsInside(region, new Point(210, 100)));
    }

    [Fact]
    public void RestoreCoordinatePolicy_UsesParentClientCoordinatesOnlyForOriginalChildWindows()
    {
        Assert.True(Invoke<bool>("OriginalWindowUsesParentClientCoordinates", WsChild));
        Assert.False(Invoke<bool>("OriginalWindowUsesParentClientCoordinates", WsPopup));
        Assert.False(Invoke<bool>("OriginalWindowUsesParentClientCoordinates", 0U));

        var source = ReadRepositoryFile("LanMountainDesktop", "Services", "WindowPassthroughService.cs");
        Assert.Contains("GWLP_HWNDPARENT", source);
        Assert.Contains("restore the owner through GWLP_HWNDPARENT", source);
    }

    [Fact]
    public void MonitorPolicy_HonorsPerWindowRepairBackoffAndHostChanges()
    {
        var now = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(Invoke<bool>(
            "ShouldAttemptNativeRepair",
            false,
            now,
            now.AddSeconds(30)));
        Assert.True(Invoke<bool>(
            "ShouldAttemptNativeRepair",
            false,
            now,
            now.AddSeconds(-1)));
        Assert.True(Invoke<bool>(
            "ShouldAttemptNativeRepair",
            true,
            now,
            now.AddMinutes(1)));
    }

    [Fact]
    public void MonitorPolicy_DoesNotRemountSafeFallbackUntilHostActuallyChanges()
    {
        var currentHost = new IntPtr(42);

        Assert.False(Invoke<bool>(
            "ShouldAttemptDesktopAttachment",
            false,
            IntPtr.Zero,
            currentHost,
            false));
        Assert.True(Invoke<bool>(
            "ShouldAttemptDesktopAttachment",
            true,
            IntPtr.Zero,
            currentHost,
            false));
        Assert.True(Invoke<bool>(
            "ShouldAttemptDesktopAttachment",
            false,
            currentHost,
            currentHost,
            true));
    }

    [Fact]
    public void HitTestCoordinates_AreResolvedFromTheLiveWindowState()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "Services", "WindowPassthroughService.cs");

        Assert.Contains("ScreenToClient(hWnd, ref screenPoint)", source);
        Assert.Contains("GetDpiForWindow(handle)", source);
        Assert.DoesNotContain("_windowScreenOrigins", source);
        Assert.DoesNotContain("_windowDpiScales", source);
        Assert.DoesNotContain("GetDpiForMonitor", source);
    }

    private static bool IsInside(WindowInteractiveRegion region, Point point)
    {
        return Invoke<bool>("IsPointInsideRegion", region, point);
    }

    private static T Invoke<T>(string methodName, params object[] arguments)
    {
        var serviceType = typeof(IWindowBottomMostService).Assembly.GetType(
            "LanMountainDesktop.Services.WindowsWindowBottomMostService",
            throwOnError: true)!;
        var method = serviceType.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotNull(method);
        return (T)method.Invoke(null, arguments)!;
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            if (File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                break;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }
}
