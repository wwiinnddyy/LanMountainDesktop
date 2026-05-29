using LanMountainDesktop.Launcher.Startup;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class HostStartupMonitorTests
{
    [Fact]
    public void InitialIpcConnectUsesStagedBackoff()
    {
        var source = ReadRepositoryFile("LanMountainDesktop.Launcher", "Startup", "HostStartupMonitor.cs");

        Assert.Contains("StartupTimeoutPolicy.InitialIpcConnectTimeout", source);
        Assert.Contains("TimeSpan.FromMilliseconds(3000)", source);
        Assert.Contains("TimeSpan.FromMilliseconds(5000)", source);
        Assert.Contains("TryConnectWithBackoffAsync", source);
    }

    [Fact]
    public void RefreshShellStatus_UsesStartupSuccessTrackerForSuccess()
    {
        var source = ReadRepositoryFile("LanMountainDesktop.Launcher", "Startup", "HostStartupMonitor.cs");

        Assert.Contains("SuccessTracker.TryResolve(shellStatus, out var successState)", source);
        var refreshBlock = source[
            source.IndexOf("RefreshShellStatusAsync", StringComparison.Ordinal) ..
            source.IndexOf("var connected = await PublicIpcConnection.TryConnectWithBackoffAsync", StringComparison.Ordinal)];
        Assert.DoesNotContain("return new StartupSuccessState", refreshBlock);
        Assert.DoesNotContain("successState = new StartupSuccessState", refreshBlock);
    }

    [Fact]
    public void BuildDelayedLoadingState_AddsSoftTimeoutItem()
    {
        var loadingState = new LoadingStateMessage
        {
            ActiveItems = [],
            OverallProgressPercent = 0,
            TotalCount = 0
        };

        var delayed = HostStartupMonitor.BuildDelayedLoadingState(
            loadingState,
            "Still starting",
            "Host is still warming up.",
            DateTimeOffset.UtcNow);

        Assert.Equal("Still starting", delayed.Message);
        Assert.Contains(delayed.ActiveItems, item => item.Id == "launcher-soft-timeout");
    }

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Unable to locate repository root.");
        }

        return File.ReadAllText(Path.Combine([directory.FullName, .. pathParts]));
    }
}
