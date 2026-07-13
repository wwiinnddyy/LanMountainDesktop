using LanMountainDesktop.Launcher.Shell;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class AirAppRuntimeBridgeTests
{
    [Fact]
    public async Task EnsureStartedAsync_StartsRuntimeAndReturnsConfirmedAvailability()
    {
        var statusCalls = 0;
        var backend = new TestBackend
        {
            GetStatusHandler = () => ++statusCalls == 1
                ? Task.FromException<AirAppRuntimeStatus>(new IOException("pipe is not ready"))
                : Task.FromResult(CreateStatus(0, hostAlive: false))
        };
        var bridge = new AirAppRuntimeBridge("C:\\app", "C:\\data", backend);

        var result = await bridge.EnsureStartedAsync();

        Assert.True(result.Available);
        Assert.Equal("started", result.Code);
        Assert.NotNull(result.Status);
        Assert.Single(backend.StartRequests);
    }

    [Fact]
    public async Task EnsureStartedAsync_WhenProcessIsNotCreated_ReturnsWithoutPolling()
    {
        var backend = new TestBackend
        {
            StartHandler = _ => null
        };
        var bridge = new AirAppRuntimeBridge("C:\\app", null, backend);

        var result = await bridge.EnsureStartedAsync();

        Assert.False(result.Available);
        Assert.Equal("process_not_created", result.Code);
        Assert.Equal(1, backend.GetStatusCalls);
        Assert.Single(backend.StartRequests);
    }

    [Fact]
    public async Task AttachHostAsync_RestartsRuntimeWhenItDisappearsBeforeAttach()
    {
        var hostProcessId = Environment.ProcessId;
        var statusCalls = 0;
        var attachCalls = 0;
        var backend = new TestBackend
        {
            GetStatusHandler = () => ++statusCalls switch
            {
                1 => Task.FromResult(CreateStatus(0, hostAlive: false)),
                2 => Task.FromException<AirAppRuntimeStatus>(new IOException("runtime exited")),
                _ => Task.FromResult(CreateStatus(0, hostAlive: false))
            },
            AttachHandler = processId => ++attachCalls == 1
                ? Task.FromException<AirAppRuntimeControlResult>(new IOException("connection closed"))
                : Task.FromResult(CreateAttachResult(processId, accepted: true, hostAlive: true))
        };
        var bridge = new AirAppRuntimeBridge("C:\\app", null, backend);

        var result = await bridge.AttachHostAsync(hostProcessId);

        Assert.True(result.Accepted);
        Assert.Equal("host_attached", result.Code);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, attachCalls);
        Assert.Single(backend.StartRequests);
    }

    [Fact]
    public async Task AttachHostAsync_RetriesAnUnconfirmedAttachResult()
    {
        var hostProcessId = Environment.ProcessId;
        var attachCalls = 0;
        var backend = new TestBackend
        {
            GetStatusHandler = () => Task.FromResult(CreateStatus(0, hostAlive: false)),
            AttachHandler = processId => Task.FromResult(++attachCalls == 1
                ? CreateAttachResult(processId + 1, accepted: true, hostAlive: true)
                : CreateAttachResult(processId, accepted: true, hostAlive: true))
        };
        var bridge = new AirAppRuntimeBridge("C:\\app", null, backend);

        var result = await bridge.AttachHostAsync(hostProcessId);

        Assert.True(result.Accepted);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(hostProcessId, result.Status!.HostProcessId);
        Assert.Empty(backend.StartRequests);
    }

    [Fact]
    public async Task AttachHostAsync_InvalidHostPidReturnsFailureWithoutRuntimeWork()
    {
        var backend = new TestBackend();
        var bridge = new AirAppRuntimeBridge("C:\\app", null, backend);

        var result = await bridge.AttachHostAsync(0);

        Assert.False(result.Accepted);
        Assert.Equal("invalid_host_pid", result.Code);
        Assert.Equal(0, result.Attempts);
        Assert.Equal(0, backend.GetStatusCalls);
        Assert.Empty(backend.StartRequests);
    }

    [Fact]
    public async Task AttachHostAsync_ReturnsObservableFailureAfterBoundedRetries()
    {
        var attachCalls = 0;
        var backend = new TestBackend
        {
            GetStatusHandler = () => Task.FromResult(CreateStatus(0, hostAlive: false)),
            AttachHandler = processId =>
            {
                attachCalls++;
                return Task.FromResult(CreateAttachResult(processId, accepted: false, hostAlive: false));
            }
        };
        var bridge = new AirAppRuntimeBridge("C:\\app", null, backend);

        var result = await bridge.AttachHostAsync(Environment.ProcessId);

        Assert.False(result.Accepted);
        Assert.Equal("host_attach_unconfirmed", result.Code);
        Assert.Equal(4, result.Attempts);
        Assert.Equal(4, attachCalls);
    }

    private static AirAppRuntimeControlResult CreateAttachResult(
        int hostProcessId,
        bool accepted,
        bool hostAlive)
    {
        return new AirAppRuntimeControlResult(
            accepted,
            accepted ? "host_attached" : "host_attach_rejected",
            "test result",
            CreateStatus(hostProcessId, hostAlive));
    }

    private static AirAppRuntimeStatus CreateStatus(int hostProcessId, bool hostAlive)
    {
        return new AirAppRuntimeStatus(
            Environment.ProcessId,
            0,
            hostProcessId,
            false,
            hostAlive,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private sealed class TestBackend : IAirAppRuntimeBridgeBackend
    {
        public Func<Task<AirAppRuntimeStatus>> GetStatusHandler { get; init; } =
            () => Task.FromException<AirAppRuntimeStatus>(new IOException("runtime unavailable"));

        public Func<int, Task<AirAppRuntimeControlResult>> AttachHandler { get; init; } =
            _ => Task.FromException<AirAppRuntimeControlResult>(new IOException("runtime unavailable"));

        public Func<AirAppRuntimeStartRequest, int?> StartHandler { get; init; } = _ => 4242;

        public List<AirAppRuntimeStartRequest> StartRequests { get; } = [];

        public int GetStatusCalls { get; private set; }

        public int? Start(AirAppRuntimeStartRequest request)
        {
            StartRequests.Add(request);
            return StartHandler(request);
        }

        public Task<AirAppRuntimeStatus> GetStatusAsync()
        {
            GetStatusCalls++;
            return GetStatusHandler();
        }

        public Task<AirAppRuntimeControlResult> AttachHostAsync(int hostProcessId)
        {
            return AttachHandler(hostProcessId);
        }

        public Task DelayAsync(TimeSpan delay) => Task.CompletedTask;
    }
}
