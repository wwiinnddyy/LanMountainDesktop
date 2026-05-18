using System.Diagnostics;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Services.AirApp;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class LauncherAirAppLifecycleServiceTests
{
    [Fact]
    public async Task OpenAsync_ReusesExistingInstanceForSameKey()
    {
        var starter = new TestAirAppProcessStarter(Process.GetCurrentProcess());
        var service = new LauncherAirAppLifecycleService(starter);
        var request = new AirAppOpenRequest(
            "whiteboard",
            BuiltInComponentIds.DesktopWhiteboard,
            "placement-1",
            Environment.ProcessId);

        var first = await service.OpenAsync(request);
        var second = await service.OpenAsync(request);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.Equal("started", first.Code);
        Assert.Equal("activated_existing", second.Code);
        Assert.Equal(1, starter.StartCount);
        Assert.Equal(first.Instance!.InstanceKey, second.Instance!.InstanceKey);
    }

    [Fact]
    public async Task OpenAsync_ReusesGlobalClockSuiteAcrossClockComponents()
    {
        var starter = new TestAirAppProcessStarter(Process.GetCurrentProcess());
        var service = new LauncherAirAppLifecycleService(starter);

        var first = await service.OpenAsync(new AirAppOpenRequest(
            "world-clock",
            BuiltInComponentIds.DesktopClock,
            "analog-placement",
            Environment.ProcessId));
        var second = await service.OpenAsync(new AirAppOpenRequest(
            "world-clock",
            BuiltInComponentIds.DesktopWorldClock,
            "world-placement",
            Environment.ProcessId));

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.Equal("started", first.Code);
        Assert.Equal("activated_existing", second.Code);
        Assert.Equal("world-clock:clock-suite:global", first.Instance!.InstanceKey);
        Assert.Equal(first.Instance.InstanceKey, second.Instance!.InstanceKey);
        Assert.Equal(1, starter.StartCount);
    }

    [Fact]
    public async Task OpenAsync_PrunesExitedRegisteredInstanceBeforeRestart()
    {
        var starter = new TestAirAppProcessStarter(Process.GetCurrentProcess());
        var service = new LauncherAirAppLifecycleService(starter);
        var instanceKey = AirAppInstanceKey.Build(
            "whiteboard",
            BuiltInComponentIds.DesktopWhiteboard,
            "placement-2");

        _ = await service.RegisterAsync(new AirAppRegistrationRequest(
            instanceKey,
            "whiteboard",
            "dead-session",
            int.MaxValue,
            "Dead Air APP",
            BuiltInComponentIds.DesktopWhiteboard,
            "placement-2"));

        var result = await service.OpenAsync(new AirAppOpenRequest(
            "whiteboard",
            BuiltInComponentIds.DesktopWhiteboard,
            "placement-2",
            Environment.ProcessId));

        Assert.True(result.Accepted);
        Assert.Equal("started", result.Code);
        Assert.Equal(1, starter.StartCount);
        Assert.Equal(Environment.ProcessId, result.Instance!.ProcessId);
    }

    [Fact]
    public async Task HasLiveAirApps_ReturnsFalseAfterUnregisteringLastInstance()
    {
        var service = new LauncherAirAppLifecycleService(new TestAirAppProcessStarter(Process.GetCurrentProcess()));
        var instanceKey = AirAppInstanceKey.Build("world-clock", BuiltInComponentIds.DesktopWorldClock, "clock-1");

        _ = await service.RegisterAsync(new AirAppRegistrationRequest(
            instanceKey,
            "world-clock",
            "session",
            Environment.ProcessId,
            "World Clock",
            BuiltInComponentIds.DesktopWorldClock,
            "clock-1"));

        Assert.True(service.HasLiveAirApps());

        _ = await service.UnregisterAsync(instanceKey, Environment.ProcessId);

        Assert.False(service.HasLiveAirApps());
    }

    [Fact]
    public void AirAppBrokerLifetime_KeepsAliveWhileRequesterIsAlive()
    {
        var service = new LauncherAirAppLifecycleService(new TestAirAppProcessStarter(null));

        Assert.True(LanMountainDesktop.Launcher.App.ShouldKeepAirAppBrokerAlive(Environment.ProcessId, service));
    }

    [Fact]
    public void AirAppBrokerLifetime_StopsWhenRequesterExitedAndNoAirAppsRemain()
    {
        var service = new LauncherAirAppLifecycleService(new TestAirAppProcessStarter(null));

        Assert.False(LanMountainDesktop.Launcher.App.ShouldKeepAirAppBrokerAlive(int.MaxValue, service));
    }

    [Fact]
    public async Task AirAppBrokerLifetime_KeepsAliveWhileAirAppIsAlive()
    {
        var service = new LauncherAirAppLifecycleService(new TestAirAppProcessStarter(null));
        var instanceKey = AirAppInstanceKey.Build("world-clock", BuiltInComponentIds.DesktopWorldClock, "clock-2");

        _ = await service.RegisterAsync(new AirAppRegistrationRequest(
            instanceKey,
            "world-clock",
            "session",
            Environment.ProcessId,
            "World Clock",
            BuiltInComponentIds.DesktopWorldClock,
            "clock-2"));

        Assert.True(LanMountainDesktop.Launcher.App.ShouldKeepAirAppBrokerAlive(int.MaxValue, service));
    }

    [Fact]
    public void CommandContext_RecognizesAirAppBrokerAsGuiCommandInDebugEnvironment()
    {
        var oldEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");

            var context = CommandContext.FromArgs(["air-app-broker", "--requester-pid", "42"]);

            Assert.True(context.IsGuiCommand);
            Assert.True(context.IsAirAppBrokerCommand);
            Assert.True(context.IsDebugMode);
            Assert.Equal(42, context.GetIntOption("requester-pid", 0));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", oldEnvironment);
        }
    }

    private sealed class TestAirAppProcessStarter : IAirAppProcessStarter
    {
        private readonly Process? _process;

        public TestAirAppProcessStarter(Process? process)
        {
            _process = process;
        }

        public int StartCount { get; private set; }

        public Process? Start(
            string appId,
            string sessionId,
            string instanceKey,
            string? sourceComponentId,
            string? sourcePlacementId)
        {
            StartCount++;
            return _process;
        }
    }
}
