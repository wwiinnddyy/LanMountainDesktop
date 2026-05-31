# Launcher Managed Air APP Lifecycle

> Superseded by `.trae/specs/air-app-runtime-container/`. Launcher no longer hosts the Air APP lifecycle broker; it pre-starts `LanMountainDesktop.AirAppRuntime`, which owns the lifecycle IPC and AirAppHost process table.

## Goal

Make Launcher the authoritative lifecycle manager for built-in Air APP processes. The desktop host requests Air APP operations through IPC, while Launcher creates, activates, tracks, and cleans up Air APP host processes.

## Behavior

- Launcher exposes `IAirAppLifecycleService` on the dedicated `LanMountainDesktop.Launcher.AirApp.v1` pipe.
- Desktop host calls Launcher IPC for `world-clock` and `whiteboard`; it does not directly start `LanMountainDesktop.AirAppHost`.
- If the dedicated pipe is unavailable, the desktop host starts Launcher with the hidden `air-app-broker --requester-pid <pid>` command and retries the Air APP request.
- `air-app-broker` starts only the Air APP lifecycle IPC broker. It bypasses OOBE, Splash, debug preview windows, and normal desktop launch orchestration.
- Launcher keeps one Air APP process per `{appId}:{sourceComponentId}:{sourcePlacementId}` key.
- AirAppHost receives Launcher pipe and instance key at startup, registers after the window opens, and unregisters on close.
- Launcher remains alive while the main desktop process or any Air APP process is alive.
- Broker mode remains alive while the requester desktop process or any Air APP process is alive; after both are gone, it exits.

## Out of Scope

- Third-party plugin-declared Air APP metadata.
- Cross-machine IPC.
- Persisting the Air APP instance table across OS reboot.
