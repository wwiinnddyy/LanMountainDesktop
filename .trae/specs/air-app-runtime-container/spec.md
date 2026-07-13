# AirApp Runtime Container

## Goal

Move built-in Air APP lifecycle management out of Launcher into a dedicated framework-dependent JIT process named `LanMountainDesktop.AirAppRuntime`, while keeping the built-in desktop entry components in the main Host and the visible Air APP windows in separate `LanMountainDesktop.AirAppHost` processes.

## Behavior

- The built-in world-clock, whiteboard, and RSS-reader desktop entry components run in the main `LanMountainDesktop` Host. Their click handlers call the Host-side `AirAppLauncherService`; they do not run inside Launcher.
- `AirAppLauncherService` sends an `AirAppOpenRequest` to `IAirAppLifecycleService` through the `LanMountainDesktop.AirAppRuntime.v1` IPC pipe.
- AirApp Runtime resolves the built-in instance key and starts or activates a separate `LanMountainDesktop.AirAppHost` process. `AirAppHost` owns and renders the visible built-in Air APP window.
- Launcher is a short-lived startup coordinator. During normal `launch` it pre-starts AirApp Runtime, starts Host, performs a bounded live-Host-PID handoff through `IAirAppRuntimeControlService`, then shuts down instead of remaining alive for the Host lifetime. A failed handoff is diagnosed and left to Host's on-demand Runtime fallback.
- AirApp Runtime exposes `IAirAppLifecycleService` and `IAirAppRuntimeControlService` on `LanMountainDesktop.AirAppRuntime.v1`.
- If the runtime pipe is unavailable, the desktop host starts `LanMountainDesktop.AirAppRuntime` directly and retries.
- AirApp Runtime keeps one AirAppHost process per resolved instance key. `world-clock` shares `world-clock:clock-suite:global`, `rss-reader` shares `rss-reader:global`, and other built-ins use `{appId}:{sourceComponentId}:{sourcePlacementId}`.
- AirApp Runtime remains alive while the startup Launcher, attached Host, requester, or any AirAppHost process is alive. After a confirmed Host attachment, Host becomes the normal runtime owner; Launcher exits after its bounded handoff work rather than extending Host lifetime.
- AirApp Runtime exits after Launcher/Host/requester are gone and no Air APP windows remain.

## Production Boundary

- `LanMountainDesktop.AirAppSdk`, `LanMountainDesktop.AirAppTemplate`, and `LanMountainDesktop.AirAppDevServer` are preview/prototype projects. The production Host, Runtime, AirAppHost, and Launcher do not reference them or load third-party `airapp.json` assemblies.
- The production AirAppHost currently selects compiled-in views for `world-clock`, `whiteboard`, and `rss-reader`; it is not a general SDK package loader.
- `.laapp` is currently routed through the plugin packaging/install path, which requires `plugin.json`. The DevServer prototype's `.laapp` output based on `airapp.json` is therefore not a production-installable Air APP package.

## Out of Scope

- Moving Air APP windows into the runtime process.
- Third-party plugin-declared Air APP metadata.
- Integrating the AirAppSdk/Template/DevServer prototype or implementing a third-party Air APP manifest/package loader.
- Persisting the Air APP instance table across OS reboot.
