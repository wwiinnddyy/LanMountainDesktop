# Window Layer Isolation

## Goal

Keep fused desktop component windows and Air APP windows in separate z-order roles.

## Behavior

- Fused desktop windows are desktop-surface windows. They may use `IWindowBottomMostService` and region passthrough, must stay attached to the Windows desktop icon host when supported, and must not cover ordinary apps.
- Air APP windows are ordinary application windows. They must not use the fused desktop bottom-most service, must not attach to the desktop icon host, and must not use global `Topmost` promotion.
- Re-showing or reloading fused desktop widgets refreshes their desktop layer after the window is visible.
- Air APP activation uses normal window activation; repeated-open foreground recovery remains owned by Launcher lifecycle activation.

## Out of Scope

- Changing Air APP lifecycle IPC.
- Changing whiteboard note sharing.
- Implementing third-party Air APP SDK behavior.
