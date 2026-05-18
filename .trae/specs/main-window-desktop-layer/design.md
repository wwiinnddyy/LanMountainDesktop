# Main Window Desktop Layer Design

## Window Roles

Lan Mountain Desktop now has three separate window-layer roles:

- `MainDesktopWindow`: the normal desktop host window. With `EnableMainWindowDesktopLayer`, this window is moved to the desktop layer so it does not cover ordinary apps.
- `FusedDesktopSurface`: fused desktop component windows such as `DesktopWidgetWindow` and `TransparentOverlayWindow`. These continue to use `IWindowBottomMostService` and their existing click-through region service.
- `AirApp`: independent Air APP windows. These are ordinary app windows and do not use desktop-layer services or global `Topmost` promotion.

## Service Boundary

`IMainWindowDesktopLayerService` is dedicated to the main window only. It does not reuse fused desktop passthrough services because the main window must stay interactive.

Windows behavior:

- Save original parent, style, and extended style before enabling.
- Try to attach the main window to the desktop icon host.
- If that host is not found, use `HWND_BOTTOM`.
- On disable, restore the saved parent and styles as best effort.

Non-Windows behavior:

- Keep a null implementation.
- Log that the platform is unsupported.

## Settings Flow

The developer settings page owns confirmation UX for conflicts:

- Fused desktop toggle and main-window desktop-layer toggle are one-way bound.
- Toggle click handlers ask for confirmation before saving conflicting states.
- The view model writes both keys together so runtime listeners receive a coherent change set.

## Runtime Flow

Main-window restore paths call `ActivateOrRefreshMainWindowLayer`.

- If `EnableMainWindowDesktopLayer` is enabled, the app refreshes the desktop-layer attachment and hides the taskbar entry.
- If disabled, the app restores ordinary activation behavior, including the existing temporary foreground promotion.

Settings changes call both fused desktop and main-window desktop-layer runtime application paths so switching modes is immediate.
