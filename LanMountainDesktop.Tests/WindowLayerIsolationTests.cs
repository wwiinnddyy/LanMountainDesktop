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

    [Fact]
    public void MainWindowDesktopLayerService_DoesNotUseFusedDesktopPassthroughBoundary()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "Services", "MainWindowDesktopLayerService.cs");

        Assert.Contains("IMainWindowDesktopLayerService", source);
        Assert.Contains("SetParent", source);
        Assert.Contains("HWND_BOTTOM", source);
        Assert.DoesNotContain("WindowBottomMostServiceFactory", source);
        Assert.DoesNotContain("IRegionPassthroughService", source);
        Assert.DoesNotContain("SetInteractiveRegions", source);
        Assert.DoesNotContain("HTTRANSPARENT", source);
        Assert.DoesNotContain("WS_EX_NOACTIVATE", source);
    }

    [Fact]
    public void MainWindowRestorePaths_UseDesktopLayerAwareActivation()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "App.axaml.cs");
        var restoreSource = ExtractMethodSource(source, "RestoreOrCreateMainWindowCore");
        var trayFallbackSource = ExtractMethodSource(source, "RecoverFromTrayUnavailable");
        var applyLayerSource = ExtractMethodSource(source, "ApplyMainWindowDesktopLayerRuntimeState");

        Assert.Contains("ActivateOrRefreshMainWindowLayer(mainWindow", restoreSource);
        Assert.DoesNotContain("Topmost = true", restoreSource);

        Assert.Contains("ActivateOrRefreshMainWindowLayer(mainWindow", trayFallbackSource);
        Assert.DoesNotContain("Topmost = true", trayFallbackSource);

        Assert.Contains("FusedDesktopManagerServiceFactory.GetOrCreate().Shutdown()", applyLayerSource);
    }

    [Fact]
    public void AppSettingsSnapshot_MainWindowDesktopLayerDefaultsToDisabled()
    {
        Assert.False(new LanMountainDesktop.Models.AppSettingsSnapshot().EnableMainWindowDesktopLayer);
    }

    [Fact]
    public void DeveloperSettings_DefinesMutuallyExclusiveDesktopLayerToggles()
    {
        var viewModelSource = ReadRepositoryFile("LanMountainDesktop", "ViewModels", "SettingsViewModels.cs");
        var pageSource = ReadRepositoryFile("LanMountainDesktop", "Views", "SettingsPages", "DevSettingsPage.axaml.cs");
        var xamlSource = ReadRepositoryFile("LanMountainDesktop", "Views", "SettingsPages", "DevSettingsPage.axaml");

        Assert.Contains("EnableMainWindowDesktopLayer", viewModelSource);
        Assert.Contains("ApplyFusedDesktopPreference", viewModelSource);
        Assert.Contains("ApplyMainWindowDesktopLayerPreference", viewModelSource);
        Assert.Contains("nameof(AppSettingsSnapshot.EnableFusedDesktop)", viewModelSource);
        Assert.Contains("nameof(AppSettingsSnapshot.EnableMainWindowDesktopLayer)", viewModelSource);

        Assert.Contains("ConfirmDesktopLayerSwitchAsync", pageSource);
        Assert.Contains("OnFusedDesktopToggleChanged", xamlSource);
        Assert.Contains("OnMainWindowDesktopLayerToggleChanged", xamlSource);
        Assert.Contains("Mode=OneWay", xamlSource);
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

    private static string ExtractMethodSource(string source, string methodName)
    {
        var methodIndex = source.IndexOf($"private bool {methodName}(", StringComparison.Ordinal);
        if (methodIndex < 0)
        {
            methodIndex = source.IndexOf($"private void {methodName}(", StringComparison.Ordinal);
        }

        Assert.True(methodIndex >= 0, $"Could not locate method '{methodName}'.");

        var braceIndex = source.IndexOf('{', methodIndex);
        Assert.True(braceIndex >= 0, $"Could not locate method body for '{methodName}'.");

        var depth = 0;
        for (var i = braceIndex; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(methodIndex, i - methodIndex + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method '{methodName}'.");
    }
}
