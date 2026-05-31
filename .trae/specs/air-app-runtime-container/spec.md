# AirApp Runtime Container

## Goal

Move built-in Air APP lifecycle management out of Launcher into a dedicated framework-dependent JIT process named `LanMountainDesktop.AirAppRuntime`.

## Behavior

- Launcher remains the user-facing entry point and pre-starts AirApp Runtime during normal `launch`.
- AirApp Runtime exposes `IAirAppLifecycleService` and `IAirAppRuntimeControlService` on `LanMountainDesktop.AirAppRuntime.v1`.
- Desktop host requests Air APP operations through AirApp Runtime IPC.
- If the runtime pipe is unavailable, the desktop host starts `LanMountainDesktop.AirAppRuntime` directly and retries.
- AirApp Runtime keeps one AirAppHost process per `{appId}:{sourceComponentId}:{sourcePlacementId}` key, with `world-clock` sharing `world-clock:clock-suite:global`.
- AirApp Runtime remains alive while Launcher, Host, requester, or any AirAppHost process is alive.
- AirApp Runtime exits after Launcher/Host/requester are gone and no Air APP windows remain.

## Out of Scope

- Moving Air APP windows into the runtime process.
- Third-party plugin-declared Air APP metadata.
- Persisting the Air APP instance table across OS reboot.
