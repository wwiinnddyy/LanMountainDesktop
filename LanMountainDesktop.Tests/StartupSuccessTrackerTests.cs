using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Startup;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class StartupSuccessTrackerTests
{
    [Fact]
    public void TryResolve_DesktopVisibleStage_SucceedsForForegroundLaunch()
    {
        var tracker = new StartupSuccessTracker(CreateContext("normal"));
        Assert.True(tracker.TryResolve(StartupStage.DesktopVisible, out var state));
        Assert.Equal("ok", state.Code);
    }

    [Fact]
    public void TryResolve_ShellStatusWithMainWindowOpened_Succeeds()
    {
        var tracker = new StartupSuccessTracker(CreateContext("normal"));
        var status = new PublicShellStatus(
            ProcessId: 1234,
            StartedAtUtc: DateTimeOffset.UtcNow,
            LaunchSource: "normal",
            ShellState: "opened",
            MainWindowCreated: true,
            MainWindowVisible: false,
            MainWindowOpened: true,
            DesktopVisible: false,
            PublicIpcReady: true,
            Tray: new PublicTrayStatus("Unavailable", false, false, false, false, 0),
            Taskbar: new PublicTaskbarStatus(false, true, false, false, false, true));

        Assert.True(tracker.TryResolve(status, out var state));
        Assert.Equal(StartupStage.Ready, state.Stage);
    }

    [Fact]
    public void TryResolve_RestartTrayPolicy_RequiresTrayAndBackground()
    {
        var tracker = new StartupSuccessTracker(CreateContext("restart", "--restart-presentation", "tray"));
        Assert.False(tracker.TryResolve(StartupStage.TrayReady, out _));
        Assert.True(tracker.TryResolve(StartupStage.BackgroundReady, out _));
        Assert.True(tracker.TryResolve(StartupStage.TrayReady, out var final));
        Assert.Equal("background_ready", final.Code);
    }

    private static CommandContext CreateContext(string launchSource, params string[] extraArgs)
    {
        var args = new List<string> { "launch", "--launch-source", launchSource };
        args.AddRange(extraArgs);
        return CommandContext.FromArgs(args.ToArray());
    }
}
