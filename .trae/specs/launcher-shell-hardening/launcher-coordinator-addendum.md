# Launcher Coordinator And Always-On Tray Addendum

## Launcher-to-launcher coordination

- Launcher reserves startup ownership in `%LocalAppData%\LanMountainDesktop\.launcher\state\startup-attempt.json` before it starts the host process.
- The reserved record includes `CoordinatorPid`, `CoordinatorPipeName`, `HeartbeatAtUtc`, `PublicIpcConnected`, `ShellStatus`, and `ReservedBeforeHostStart`.
- Only the active coordinator may call `Process.Start()` for the host. Secondary Launchers attach to the coordinator pipe and request desktop activation or status.
- If the coordinator heartbeat is newer than `10s` and the coordinator pid is alive, a new Launcher must not take over.
- If the coordinator is stale, the next Launcher may take over the same pending attempt instead of creating a second host attempt.
- Normal launches probe Host Public IPC first. If a host is already running, Launcher activates that instance and exits without starting another host.

## Finer shell status

- Public shell IPC exposes `GetShellStatusAsync()`, `ActivateMainWindowWithStatusAsync()`, `EnsureTrayReadyAsync()`, and `EnsureTaskbarEntryAsync()`.
- `PublicShellStatus` separates process, shell state, main-window visibility, tray health, taskbar-entry health, and Public IPC readiness.
- Launcher success/failure details must include coordinator pid, attempt id, host pid, Public IPC status, tray state, and taskbar usability when available.

## Always-on tray and taskbar repair

- The tray icon and menu are mandatory application-liveness indicators and are not controlled by user settings.
- Tray watchdog starts during shell initialization and keeps running until application exit.
- `ShowInTaskbar=true` means hidden/background states prefer `MinimizedToTaskbar`; it never disables the tray.
- `ShowInTaskbar=false` is the only mode that may enter pure `TrayOnly`, and only after `TrayReady`.
- When taskbar entry is requested but missing, shell repair recreates or shows the main window minimized with `ShowInTaskbar=true` while keeping the tray visible.

## Regression coverage

- Unit tests cover active coordinator rejection, stale heartbeat takeover, and host-pid assignment after a reserved attempt.
- Manual QA still needs multi-process Launcher concurrency and real tray loss simulation on Windows.
