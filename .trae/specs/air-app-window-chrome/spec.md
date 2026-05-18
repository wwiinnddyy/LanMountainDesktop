# Air APP Window Chrome

## Goal

Give Air APPs explicit window chrome modes so title bars, fullscreen windows, borderless windows, tool windows, and future background-only apps are configured by the Air APP host instead of ad hoc component code.

## Behavior

- Air APP host resolves an `AirAppWindowDescriptor` from launch options before creating content.
- Supported chrome modes are `Standard`, `Borderless`, `FullScreen`, `Tool`, and `BackgroundOnly`.
- `Standard` uses FluentAvalonia `FAAppWindow` title-bar chrome and normal app-window behavior.
- `Borderless` removes title-bar chrome while keeping a normal app window surface.
- `FullScreen` removes title-bar chrome and enters fullscreen.
- `Tool` keeps FluentAvalonia title-bar chrome but disables resizing and hides the taskbar entry.
- `BackgroundOnly` is reserved for a later background Air APP lifecycle and is not used by built-in v1 apps.
- Built-in `world-clock` uses `Standard`; built-in `whiteboard` uses `FullScreen`.

## Out of Scope

- Third-party plugin Air APP declarations.
- Replacing Launcher lifecycle IPC.
- Moving title-bar rendering into desktop components.
