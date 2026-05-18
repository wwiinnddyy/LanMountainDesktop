# Main Window Desktop Layer Tasks

- [x] Add `EnableMainWindowDesktopLayer` to app settings with a disabled default.
- [x] Add developer settings UI and localization strings.
- [x] Add confirmation flow for mutual exclusion with fused desktop.
- [x] Add a dedicated main-window desktop-layer service.
- [x] Wire main-window creation, restore, tray fallback, settings changes, and shutdown cleanup to the service.
- [x] Keep Air APP windows outside this layer service.
- [x] Add static regression tests for settings, restore paths, and service boundaries.
- [ ] Perform manual Windows z-order validation with real apps.
