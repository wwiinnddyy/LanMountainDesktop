# Checklist

- [x] `LanMountainDesktop.AirAppRuntime` is included in `LanMountainDesktop.slnx`.
- [x] Launcher no longer hosts `IAirAppLifecycleService`.
- [x] Launcher performs a bounded Host attach after startup, records the outcome, and exits without waiting for the Host process lifetime.
- [x] Built-in world-clock, whiteboard, and RSS-reader entry components execute in Host and call Host-side `AirAppLauncherService`.
- [x] AirApp Runtime starts AirAppHost, and AirAppHost renders only the compiled-in built-in Air APP views.
- [x] Host fallback starts `LanMountainDesktop.AirAppRuntime`, not `LanMountainDesktop.Launcher air-app-broker`.
- [x] Production Host/Runtime/AirAppHost/Launcher do not reference AirAppSdk, AirAppTemplate, or AirAppDevServer and do not scan third-party `airapp.json` packages.
- [x] Documentation does not present the prototype `.laapp`/`airapp.json` output as compatible with the production `plugin.json` package installer.
- [x] AirApp Runtime is explicitly non-AOT and framework-dependent.
- [x] `dotnet build LanMountainDesktop.slnx -c Debug` passes.
- [x] Related AirApp Runtime tests pass.
- [x] `dotnet test LanMountainDesktop.slnx -c Debug` passes.
