# Air APP Whiteboard

## Goal

Allow the built-in whiteboard desktop components to open a full-screen Air APP that runs in `LanMountainDesktop.AirAppHost` and reuses the same persisted whiteboard note as the source component instance.

## Scope

- Add a toolbar surface-mode button to `WhiteboardWidget`.
- In component mode, the button opens the `whiteboard` Air APP through `IAirAppLauncherService`.
- In Air APP mode, the same button saves the current note and closes the Air APP window.
- `DesktopWhiteboard` and `DesktopBlackboardLandscape` share the same mechanism and keep using their component id plus placement id as the note identity.
- `LanMountainDesktop.AirAppHost` may reference the host assembly to reuse built-in UI controls, but the host app must not reference AirAppHost as a normal assembly dependency.

## Out of Scope

- Third-party Air APP SDK declarations.
- Whiteboard feature rewrites or alternate whiteboard persistence.
- Taskbar minimization behavior; v1 closes the Air APP window when the user exits from the bottom toolbar.

## Acceptance

- Building the main app also builds and copies `LanMountainDesktop.AirAppHost` output.
- Clicking the whiteboard toolbar full-screen button launches a separate AirAppHost process.
- Repeated opens of the same whiteboard component instance activate the existing process instead of spawning duplicates.
- Closing and reopening the Air APP keeps the same whiteboard contents.
