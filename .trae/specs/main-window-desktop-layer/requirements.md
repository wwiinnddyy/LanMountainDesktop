# Main Window Desktop Layer

## Requirements

- Add a developer option named `EnableMainWindowDesktopLayer`.
- When enabled, the main Lan Mountain desktop window behaves like a desktop-surface window: ordinary application windows can stay above it.
- The feature is implemented as desktop-layer or bottom placement, not as `Topmost`.
- The option is mutually exclusive with `EnableFusedDesktop`.
- Enabling main-window desktop layer while fused desktop is enabled must ask for confirmation, then disable fused desktop on confirm or roll back on cancel.
- Enabling fused desktop while main-window desktop layer is enabled must ask for confirmation, then disable main-window desktop layer on confirm or roll back on cancel.
- Air APP windows remain ordinary application windows and must not be attached to the desktop layer.
- On Windows, the main window should attach to the desktop icon host when available and fall back to `HWND_BOTTOM` when unavailable.
- On non-Windows platforms, the setting may exist but the layer service is a no-op and must not throw.

## Acceptance

- Opening another app above Lan Mountain Desktop keeps that app visible when main-window desktop layer is enabled.
- Restoring the main window from tray keeps the desktop-layer behavior and does not perform a temporary `Topmost` promotion.
- Turning the option off restores normal main-window behavior as far as possible.
- Fused desktop component windows keep their existing bottom-most behavior and remain isolated from the main-window service.
