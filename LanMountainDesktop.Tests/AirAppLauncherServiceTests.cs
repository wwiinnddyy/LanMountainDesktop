using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class AirAppLauncherServiceTests
{
    [Fact]
    public void BuildOpenRequest_IncludesWorldClockSourceContext()
    {
        var request = AirAppLauncherService.BuildOpenRequest(
            AirAppLauncherService.WorldClockAppId,
            BuiltInComponentIds.DesktopWorldClock,
            "placement-7",
            42);

        Assert.Equal("world-clock", request.AppId);
        Assert.Equal(BuiltInComponentIds.DesktopWorldClock, request.SourceComponentId);
        Assert.Equal("placement-7", request.SourcePlacementId);
        Assert.Equal(42, request.RequesterProcessId);
    }

    [Fact]
    public void BuildOpenRequest_IncludesAnalogClockSourceContext()
    {
        var request = AirAppLauncherService.BuildOpenRequest(
            AirAppLauncherService.WorldClockAppId,
            BuiltInComponentIds.DesktopClock,
            "analog-placement",
            43);

        Assert.Equal("world-clock", request.AppId);
        Assert.Equal(BuiltInComponentIds.DesktopClock, request.SourceComponentId);
        Assert.Equal("analog-placement", request.SourcePlacementId);
        Assert.Equal(43, request.RequesterProcessId);
    }

    [Fact]
    public void BuildOpenRequest_NormalizesEmptyOptionalContext()
    {
        var request = AirAppLauncherService.BuildOpenRequest(
            AirAppLauncherService.WorldClockAppId,
            null,
            " ",
            42);

        Assert.Equal("world-clock", request.AppId);
        Assert.Null(request.SourceComponentId);
        Assert.Null(request.SourcePlacementId);
        Assert.Equal(42, request.RequesterProcessId);
    }

    [Fact]
    public void BuildOpenRequest_IncludesWhiteboardSourceContext()
    {
        var request = AirAppLauncherService.BuildOpenRequest(
            AirAppLauncherService.WhiteboardAppId,
            BuiltInComponentIds.DesktopWhiteboard,
            "whiteboard-placement",
            99);

        Assert.Equal("whiteboard", request.AppId);
        Assert.Equal(BuiltInComponentIds.DesktopWhiteboard, request.SourceComponentId);
        Assert.Equal("whiteboard-placement", request.SourcePlacementId);
        Assert.Equal(99, request.RequesterProcessId);
    }

    [Fact]
    public void BuildSingleInstanceKey_UsesWhiteboardComponentAndPlacement()
    {
        var key = AirAppLauncherService.BuildSingleInstanceKey(
            AirAppLauncherService.WhiteboardAppId,
            BuiltInComponentIds.DesktopBlackboardLandscape,
            "placement-3");

        Assert.Equal(
            $"whiteboard:{BuiltInComponentIds.DesktopBlackboardLandscape}:placement-3",
            key);
    }

    [Fact]
    public void CreateBrokerStartInfo_UsesAirAppBrokerCommandAndRequesterPid()
    {
        var startInfo = AirAppLauncherService.CreateBrokerStartInfo(
            @"C:\Apps\LanMountainDesktop.Launcher.exe",
            12345);

        Assert.Equal(@"C:\Apps\LanMountainDesktop.Launcher.exe", startInfo.FileName);
        Assert.Equal(@"C:\Apps", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(
            ["air-app-broker", "--requester-pid", "12345"],
            startInfo.ArgumentList);
    }
}
