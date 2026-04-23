# Tray Menu Shutdown Addendum

## Requirements

- Tray menu `Exit App` must commit an irreversible host shutdown request.
- Once shutdown is committed, tray menu actions must not reopen the desktop, settings window, or component library.
- Shutdown cleanup must release Public IPC, plugin runtime, tray icon, fused desktop edit UI, telemetry resources, and the single-instance lock before the forced-exit deadline.
- Forced process termination must be scheduled when the shutdown request is accepted, not only after Avalonia lifetime exit.
- Restart must preserve `RestartRequested` intent and must not route through an exit path that overwrites it.
- Fused desktop component library menu activation must reuse the existing library window and must exit edit mode if opening fails.

## Acceptance

- Selecting `Exit App` from the tray leaves no background host process and allows a later Launcher start to acquire the single-instance lock.
- Selecting `Restart App` starts the Launcher or upgrade helper once, then shuts down the old host as a restart.
- Repeated tray clicks during shutdown are ignored and logged.
- Repeated component-library clicks focus the existing window instead of opening duplicates.
