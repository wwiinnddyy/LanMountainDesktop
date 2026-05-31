# Launcher Coordinator

LanMountainDesktop Launcher uses a per-user coordinator to prevent duplicate host startup.

## Rules

- A Launcher reserves `%LocalAppData%\LanMountainDesktop\.launcher\state\startup-attempt.json` before starting the host.
- The active record stores coordinator pid, coordinator pipe name, heartbeat, host pid, Public IPC state, and shell status.
- Only the active coordinator may start the host process.
- Secondary Launchers attach to the coordinator and request desktop activation.
- A coordinator is considered live while its pid exists and its heartbeat is newer than `10s`.
- Normal launch probes Host Public IPC first; if the host is already running, Launcher activates it and exits.

## Tray And Taskbar

- Tray icon and tray menu are mandatory and are not controlled by user settings.
- Tray watchdog starts with the shell and runs until process exit.
- `ShowInTaskbar=true` affects only the main-window taskbar entry.
- When `ShowInTaskbar=true`, background mode uses a minimized taskbar entry while keeping tray visible.
- Pure `TrayOnly` is allowed only when `ShowInTaskbar=false` and tray is ready.

## Public Shell IPC

Launcher and external callers can use:

- `GetShellStatusAsync()`
- `ActivateMainWindowWithStatusAsync()`
- `EnsureTrayReadyAsync()`
- `EnsureTaskbarEntryAsync()`

These APIs report process, shell, tray, taskbar, and activation state separately so callers do not infer health from window visibility alone.

## Air APP Lifecycle

- `LanMountainDesktop.AirAppRuntime` is the Air APP lifecycle manager.
- The desktop host requests Air APP operations through `IAirAppLifecycleService` on the dedicated `LanMountainDesktop.AirAppRuntime.v1` IPC pipe.
- Launcher pre-starts `LanMountainDesktop.AirAppRuntime`; when the dedicated pipe is unavailable, the desktop host starts the runtime directly and retries the request.
- AirApp Runtime, not Launcher, owns the Air APP lifecycle IPC host and AirAppHost process table.
- AirApp Runtime creates, activates, tracks, and closes Air APP host processes by instance key: `{appId}:{sourceComponentId}:{sourcePlacementId}`.
- `LanMountainDesktop.AirAppHost` registers itself with AirApp Runtime after its window opens and unregisters on close; Runtime also prunes exited processes.
- Launcher waits only for the desktop host startup path. AirApp Runtime remains alive while Launcher/Host/requester or any Air APP process is alive, then exits after all are gone.
