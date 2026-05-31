# Checklist

- [x] `LanMountainDesktop.AirAppRuntime` is included in `LanMountainDesktop.slnx`.
- [x] Launcher no longer hosts `IAirAppLifecycleService`.
- [x] Host fallback starts `LanMountainDesktop.AirAppRuntime`, not `LanMountainDesktop.Launcher air-app-broker`.
- [x] AirApp Runtime is explicitly non-AOT and framework-dependent.
- [x] `dotnet build LanMountainDesktop.slnx -c Debug` passes.
- [x] Related AirApp Runtime tests pass.
- [x] `dotnet test LanMountainDesktop.slnx -c Debug` passes.
