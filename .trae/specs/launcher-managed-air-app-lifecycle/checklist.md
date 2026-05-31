# Checklist

> Superseded by `.trae/specs/air-app-runtime-container/`; the checked items below describe the former Launcher-managed implementation.

- [x] `LanMountainDesktop.Shared.IPC` builds in Debug.
- [x] `LanMountainDesktop.Launcher` builds in Debug.
- [x] `LanMountainDesktop` builds in Debug.
- [x] `LanMountainDesktop.AirAppHost` builds in Debug.
- [x] `LanMountainDesktop.Tests` builds in Debug.
- [x] Air APP launcher and lifecycle unit tests pass.
- [x] Direct-host fallback starts Launcher in `air-app-broker` mode instead of debug/normal launch mode.
- [ ] Manual process-lifetime verification with the running desktop.
