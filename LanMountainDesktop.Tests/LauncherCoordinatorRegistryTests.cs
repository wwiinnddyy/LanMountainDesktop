using System.Text.Json.Nodes;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Shared.Contracts.Launcher;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class LauncherCoordinatorRegistryTests
{
    [Fact]
    public void TryReserveCoordinator_WhenActiveCoordinatorExists_ReturnsActiveAttempt()
    {
        using var temp = TemporaryAttemptState.Create();
        var firstRegistry = new StartupAttemptRegistry(temp.StatePath);
        var secondRegistry = new StartupAttemptRegistry(temp.StatePath);

        Assert.True(firstRegistry.TryReserveCoordinator(
            "normal",
            "Foreground",
            "pipe-a",
            out var firstAttempt,
            out var firstActive));
        Assert.Null(firstActive);

        Assert.False(secondRegistry.TryReserveCoordinator(
            "normal",
            "Foreground",
            "pipe-b",
            out _,
            out var secondActive));

        Assert.NotNull(secondActive);
        Assert.Equal(firstAttempt.AttemptId, secondActive.AttemptId);
        Assert.Equal("pipe-a", secondActive.CoordinatorPipeName);
        Assert.Equal(Environment.ProcessId, secondActive.CoordinatorPid);
    }

    [Fact]
    public void TryReserveCoordinator_WhenHeartbeatIsStale_TakesOverAttempt()
    {
        using var temp = TemporaryAttemptState.Create();
        var firstRegistry = new StartupAttemptRegistry(temp.StatePath);
        var secondRegistry = new StartupAttemptRegistry(temp.StatePath);

        Assert.True(firstRegistry.TryReserveCoordinator(
            "normal",
            "Foreground",
            "pipe-a",
            out var firstAttempt,
            out _));
        temp.SetHeartbeat(DateTimeOffset.UtcNow.AddSeconds(-30));

        Assert.True(secondRegistry.TryReserveCoordinator(
            "normal",
            "Foreground",
            "pipe-b",
            out var reservedAttempt,
            out var activeAttempt));

        Assert.Null(activeAttempt);
        Assert.Equal(firstAttempt.AttemptId, reservedAttempt.AttemptId);
        Assert.Equal("pipe-b", reservedAttempt.CoordinatorPipeName);
    }

    [Fact]
    public void AssignOwnedHostProcess_ClearsReservedBeforeHostStart()
    {
        using var temp = TemporaryAttemptState.Create();
        var registry = new StartupAttemptRegistry(temp.StatePath);

        Assert.True(registry.TryReserveCoordinator(
            "normal",
            "Foreground",
            "pipe-a",
            out var reservedAttempt,
            out _));
        Assert.True(reservedAttempt.ReservedBeforeHostStart);

        var assignedAttempt = registry.AssignOwnedHostProcess(
            Environment.ProcessId,
            StartupStage.Initializing,
            "host assigned");

        Assert.Equal(Environment.ProcessId, assignedAttempt.HostPid);
        Assert.False(assignedAttempt.ReservedBeforeHostStart);
    }

    private sealed class TemporaryAttemptState : IDisposable
    {
        private TemporaryAttemptState(string directory)
        {
            Directory = directory;
            StatePath = Path.Combine(directory, "startup-attempt.json");
        }

        public string Directory { get; }

        public string StatePath { get; }

        public static TemporaryAttemptState Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "LanMountainDesktop.LauncherCoordinatorTests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            return new TemporaryAttemptState(directory);
        }

        public void SetHeartbeat(DateTimeOffset heartbeatAtUtc)
        {
            var node = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
            node["heartbeatAtUtc"] = heartbeatAtUtc.ToString("O");
            File.WriteAllText(StatePath, node.ToJsonString());
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
