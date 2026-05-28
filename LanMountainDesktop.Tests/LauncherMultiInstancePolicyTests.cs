using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Startup;
using LanMountainDesktop.Models;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class LauncherMultiInstancePolicyTests
{
    [Fact]
    public void AppSettingsSnapshot_DefaultsToNotifyAndOpenDesktop()
    {
        Assert.Equal(
            MultiInstanceLaunchBehavior.NotifyAndOpenDesktop,
            new AppSettingsSnapshot().MultiInstanceLaunchBehavior);
    }

    [Fact]
    public void ShouldProbeExistingHostBeforeLaunch_ReturnsTrue_ForNormalLaunch()
    {
        var context = CommandContext.FromArgs(["launch"]);

        Assert.True(HostActivationPolicy.ShouldProbeExistingHostBeforeLaunch(context));
    }

    [Fact]
    public void ShouldProbeExistingHostBeforeLaunch_ReturnsFalse_ForRestartLaunch()
    {
        var context = CommandContext.FromArgs([
            "launch",
            $"--{LauncherIpcConstants.LaunchSourceOptionName}=restart"
        ]);

        Assert.False(HostActivationPolicy.ShouldProbeExistingHostBeforeLaunch(context));
    }

    [Fact]
    public void ActivationExitCodes_AreClassifiedSeparatelyFromEarlyHostExit()
    {
        Assert.True(HostActivationPolicy.IsSuccessfulActivationExitCode(HostExitCodes.SecondaryActivationSucceeded));
        Assert.True(HostActivationPolicy.IsFailedActivationExitCode(HostExitCodes.SecondaryActivationFailed));
        Assert.True(HostActivationPolicy.IsFailedActivationExitCode(HostExitCodes.RestartLockNotAcquired));
        Assert.False(HostActivationPolicy.IsFailedActivationExitCode(1));
    }

    [Fact]
    public void IsRecoverableActivationFailure_ReturnsTrue_WhenPublicIpcIsReadyButShellIsPending()
    {
        var activation = new PublicShellActivationResult(
            false,
            "shell_not_ready",
            "Desktop shell is still initializing.",
            CreateShellStatus(
                publicIpcReady: true,
                mainWindowOpened: false,
                desktopVisible: false));

        Assert.True(HostActivationPolicy.IsRecoverableActivationFailure(activation));
    }

    [Fact]
    public void IsRecoverableActivationFailure_ReturnsFalse_WhenShutdownIsInProgress()
    {
        var activation = new PublicShellActivationResult(
            false,
            "shutdown_in_progress",
            "Desktop is shutting down.",
            CreateShellStatus(
                publicIpcReady: true,
                mainWindowOpened: false,
                desktopVisible: false));

        Assert.False(HostActivationPolicy.IsRecoverableActivationFailure(activation));
    }

    [Fact]
    public void IsExistingHostReadyForLauncherDecision_RequiresPublicIpcReady()
    {
        Assert.False(HostActivationPolicy.IsExistingHostReadyForLauncherDecision(null));
        Assert.False(HostActivationPolicy.IsExistingHostReadyForLauncherDecision(CreateShellStatus(
            publicIpcReady: false,
            mainWindowOpened: true,
            desktopVisible: true)));
        Assert.True(HostActivationPolicy.IsExistingHostReadyForLauncherDecision(CreateShellStatus(
            publicIpcReady: true,
            mainWindowOpened: true,
            desktopVisible: true)));
    }


    private static PublicShellStatus CreateShellStatus(
        bool publicIpcReady,
        bool mainWindowOpened,
        bool desktopVisible)
    {
        return new PublicShellStatus(
            ProcessId: Environment.ProcessId,
            StartedAtUtc: DateTimeOffset.UtcNow,
            LaunchSource: "normal",
            ShellState: mainWindowOpened ? "opened" : "initializing",
            MainWindowCreated: mainWindowOpened,
            MainWindowVisible: desktopVisible,
            MainWindowOpened: mainWindowOpened,
            DesktopVisible: desktopVisible,
            PublicIpcReady: publicIpcReady,
            Tray: new PublicTrayStatus(
                State: "Unavailable",
                IsReady: false,
                HasIcon: false,
                HasMenu: false,
                IsVisible: false,
                ConsecutiveRecoveryFailures: 0),
            Taskbar: new PublicTaskbarStatus(
                RequestedBySettings: false,
                MainWindowExists: mainWindowOpened,
                MainWindowShowInTaskbar: mainWindowOpened,
                MainWindowVisible: desktopVisible,
                MainWindowMinimized: false,
                IsUsable: mainWindowOpened));
    }
}
