# Tasks

- [x] Add shared AirApp Runtime IPC/control contracts.
- [x] Add shared AirApp Runtime path resolver and process starter.
- [x] Add `LanMountainDesktop.AirAppRuntime` as a framework-dependent JIT process.
- [x] Move Air APP lifecycle service out of Launcher.
- [x] Keep built-in Air APP entry components in Host and route their clicks to AirApp Runtime over IPC.
- [x] Make Launcher pre-start AirApp Runtime, perform a bounded Host PID handoff after launch, and exit instead of tracking Host lifetime.
- [x] Keep visible built-in Air APP windows in separate AirAppHost processes managed by AirApp Runtime.
- [x] Make Host fallback start AirApp Runtime instead of Launcher broker.
- [x] Remove Launcher `air-app-broker` command handling.
- [x] Update packaging scripts and release workflow to include AirApp Runtime.
- [x] Document that AirAppSdk, AirAppTemplate, and AirAppDevServer are not yet connected to the production loading path.
- [x] Update unit tests and architecture/package assertions.
