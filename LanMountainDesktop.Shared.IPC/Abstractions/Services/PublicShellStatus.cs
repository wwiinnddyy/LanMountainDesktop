namespace LanMountainDesktop.Shared.IPC.Abstractions.Services;

public sealed record PublicShellStatus(
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    string LaunchSource,
    string ShellState,
    bool MainWindowCreated,
    bool MainWindowVisible,
    bool MainWindowOpened,
    bool DesktopVisible,
    bool PublicIpcReady,
    PublicTrayStatus Tray,
    PublicTaskbarStatus Taskbar);

public sealed record PublicTrayStatus(
    string State,
    bool IsReady,
    bool HasIcon,
    bool HasMenu,
    bool IsVisible,
    int ConsecutiveRecoveryFailures);

public sealed record PublicTaskbarStatus(
    bool RequestedBySettings,
    bool MainWindowExists,
    bool MainWindowShowInTaskbar,
    bool MainWindowVisible,
    bool MainWindowMinimized,
    bool IsUsable);

public sealed record PublicShellActivationResult(
    bool Accepted,
    string Code,
    string Message,
    PublicShellStatus Status);
