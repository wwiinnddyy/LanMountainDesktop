using System.Diagnostics;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class AirAppRuntimeLifecycleServiceTests
{
    [Fact]
    public async Task OpenAsync_ReusesExistingInstanceForSameKey()
    {
        var starter = new TestAirAppProcessStarter(Process.GetCurrentProcess());
        var service = new AirAppLifecycleService(starter);
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
        var service = new AirAppLifecycleService(starter);

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
        var service = new AirAppLifecycleService(starter);
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
        var service = new AirAppLifecycleService(new TestAirAppProcessStarter(Process.GetCurrentProcess()));
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
    public void RuntimeLifetime_KeepsAliveWhileRequesterIsAlive()
    {
        var service = new AirAppLifecycleService(new TestAirAppProcessStarter(null));
        var lifetime = new AirAppRuntimeLifetime(
            new AirAppRuntimeOptions(null, null, 0, Environment.ProcessId),
            service);

        Assert.True(lifetime.ShouldKeepAlive());
    }

    [Fact]
    public void RuntimeLifetime_StopsWhenNoProcessOrAirAppsRemain()
    {
        var service = new AirAppLifecycleService(new TestAirAppProcessStarter(null));
        var lifetime = new AirAppRuntimeLifetime(
            new AirAppRuntimeOptions(null, null, int.MaxValue, int.MaxValue),
            service);

        Assert.False(lifetime.ShouldKeepAlive());
    }

    [Fact]
    public async Task RuntimeLifetime_KeepsAliveWhileAirAppIsAlive()
    {
        var service = new AirAppLifecycleService(new TestAirAppProcessStarter(null));
        var instanceKey = AirAppInstanceKey.Build("world-clock", BuiltInComponentIds.DesktopWorldClock, "clock-2");
        var lifetime = new AirAppRuntimeLifetime(
            new AirAppRuntimeOptions(null, null, int.MaxValue, int.MaxValue),
            service);

        _ = await service.RegisterAsync(new AirAppRegistrationRequest(
            instanceKey,
            "world-clock",
            "session",
            Environment.ProcessId,
            "World Clock",
            BuiltInComponentIds.DesktopWorldClock,
            "clock-2"));

        Assert.True(lifetime.ShouldKeepAlive());
    }

    [Fact]
    public async Task RuntimeControl_AttachesHostProcess()
    {
        var service = new AirAppLifecycleService(new TestAirAppProcessStarter(null));
        var lifetime = new AirAppRuntimeLifetime(
            new AirAppRuntimeOptions(null, null, int.MaxValue, int.MaxValue),
            service);
        var control = new AirAppRuntimeControlService(lifetime);

        var result = await control.AttachHostAsync(Environment.ProcessId);

        Assert.True(result.Accepted);
        Assert.Equal(Environment.ProcessId, result.Status.HostProcessId);
        Assert.True(result.Status.HostProcessAlive);
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
