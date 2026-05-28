using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Startup;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class HostActivationPolicyTests
{
    [Theory]
    [InlineData("launch", "normal", true)]
    [InlineData("launch", "restart", false)]
    [InlineData("apply-update", "normal", false)]
    public void ShouldProbeExistingHostBeforeLaunch_RespectsLaunchSource(
        string command,
        string launchSource,
        bool expected)
    {
        var context = CommandContext.FromArgs([command, "--launch-source", launchSource]);
        Assert.Equal(expected, HostActivationPolicy.ShouldProbeExistingHostBeforeLaunch(context));
    }

    [Fact]
    public void IsRecoverableActivationFailure_AllowsStartupPendingWhenIpcReady()
    {
        var activation = new PublicShellActivationResult(
            false,
            "startup_pending",
            "pending",
            new PublicShellStatus(
                ProcessId: 1,
                StartedAtUtc: DateTimeOffset.UtcNow,
                LaunchSource: "normal",
                ShellState: "initializing",
                MainWindowCreated: false,
                MainWindowVisible: false,
                MainWindowOpened: false,
                DesktopVisible: false,
                PublicIpcReady: true,
                Tray: new PublicTrayStatus("Unavailable", false, false, false, false, 0),
                Taskbar: new PublicTaskbarStatus(false, false, false, false, false, false)));

        Assert.True(HostActivationPolicy.IsRecoverableActivationFailure(activation));
    }
}
