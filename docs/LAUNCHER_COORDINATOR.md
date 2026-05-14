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

- Launcher is also the Air APP lifecycle manager.
- The desktop host requests Air APP operations through `IAirAppLifecycleService` on the dedicated `LanMountainDesktop.Launcher.AirApp.v1` IPC pipe.
- When the dedicated pipe is unavailable, the desktop host starts `LanMountainDesktop.Launcher.exe air-app-broker --requester-pid <pid>` and retries the request.
- `air-app-broker` is a hidden internal command that starts only the Air APP lifecycle IPC host. It bypasses OOBE, Splash, debug preview windows, and normal desktop launch orchestration.
- Launcher creates, activates, tracks, and closes Air APP host processes by instance key: `{appId}:{sourceComponentId}:{sourcePlacementId}`.
- `LanMountainDesktop.AirAppHost` registers itself with Launcher after its window opens and unregisters on close; Launcher also prunes exited processes.
- Launcher remains alive while either the desktop host process or any Air APP process is alive.
- Broker mode remains alive while the requester process or any Air APP process is alive, then exits after both are gone.
