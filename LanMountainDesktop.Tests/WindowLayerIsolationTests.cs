using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class WindowLayerIsolationTests
{
    [Fact]
    public void AirAppWindow_DoesNotUseDesktopBottomMostOrTopmostPromotion()
    {
        var source = ReadRepositoryFile("LanMountainDesktop.AirAppHost", "AirAppWindow.axaml.cs");

        Assert.DoesNotContain("WindowBottomMostServiceFactory", source);
        Assert.DoesNotContain("IWindowBottomMostService", source);
        Assert.DoesNotContain("SendToBottom", source);
        Assert.DoesNotContain("Topmost = true", source);
        Assert.DoesNotContain("Topmost=true", source);
    }

    [Fact]
    public void AirAppWindowDescriptor_DefinesSupportedChromeModes()
    {
        var source = ReadRepositoryFile("LanMountainDesktop.AirAppHost", "AirAppWindowDescriptor.cs");

        Assert.Contains("AirAppWindowChromeMode", source);
        Assert.Contains("Standard", source);
        Assert.Contains("Borderless", source);
        Assert.Contains("FullScreen", source);
        Assert.Contains("Tool", source);
        Assert.Contains("BackgroundOnly", source);
    }

    [Fact]
    public void AirAppWindowDescriptor_MapsBuiltInAppsToExpectedChromeModes()
    {
        var source = ReadRepositoryFile("LanMountainDesktop.AirAppHost", "AirAppWindowDescriptor.cs");

        Assert.Contains("AirAppLaunchOptions.WorldClockAppId", source);
        Assert.Contains("AirAppWindowChromeMode.Standard", source);
        Assert.Contains("AirAppLaunchOptions.WhiteboardAppId", source);
        Assert.Contains("AirAppWindowChromeMode.FullScreen", source);
    }

    [Fact]
    public void FusedDesktopWindows_KeepDesktopBottomMostBoundary()
    {
        var desktopWidgetWindow = ReadRepositoryFile("LanMountainDesktop", "Views", "DesktopWidgetWindow.axaml.cs");
        var transparentOverlayWindow = ReadRepositoryFile("LanMountainDesktop", "Views", "TransparentOverlayWindow.axaml.cs");

        Assert.Contains("WindowBottomMostServiceFactory.GetOrCreate()", desktopWidgetWindow);
        Assert.Contains("RefreshDesktopLayer", desktopWidgetWindow);
        Assert.Contains("SendToBottom", desktopWidgetWindow);

        Assert.Contains("WindowBottomMostServiceFactory.GetOrCreate()", transparentOverlayWindow);
        Assert.Contains("RefreshDesktopLayer", transparentOverlayWindow);
        Assert.Contains("SendToBottom", transparentOverlayWindow);
    }

    [Fact]
    public void FusedDesktopManager_RefreshesDesktopLayerAfterShowingWidgets()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "Services", "FusedDesktopManagerService.cs");

        Assert.Contains("existingWindow.RefreshDesktopLayer()", source);
        Assert.Contains("window.RefreshDesktopLayer()", source);
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
