# External IPC Public API Spec

## Goal

Provide a single `dotnetCampus.Ipc` based external integration layer for:

- Host public APIs
- Launcher/OOBE startup progress and loading-state notifications
- plugin-contributed public services and live event push

## Delivered

- `LanMountainDesktop.Shared.IPC` project
- `[IpcPublic]` based built-in public contracts
- `PublicIpcHostService` and `LanMountainDesktopIpcClient`
- Launcher migrated to Host public IPC notifications
- Plugin SDK public IPC contribution API
- Host runtime integration for plugin public IPC services

## Out of Scope

- plugin process isolation
- non-.NET strong-typed public IPC clients
- live plugin public service removal without restart
