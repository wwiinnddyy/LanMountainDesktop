# Settings Window Fluent Shell Redesign

## Goal

Rebuild the settings window as an independent Fluent shell with a custom titlebar, titlebar hamburger menu, persistent side navigation, search, and Avalonia-standard system material support.

## Requirements

- Keep the existing independent settings-window lifecycle: open-or-focus, no owner anchor, own taskbar entry.
- Use a 48 DIP titlebar with Back, pane toggle, icon/title, search, restart action, more menu, and caption-button spacer.
- Keep `FANavigationView` as the primary navigation surface with `OpenPaneLength` around 283 DIP.
- Move the compact/minimal pane toggle from the navigation footer into the titlebar.
- Add search over built-in settings pages and settings expanders; selecting a result navigates, expands, focuses, and highlights.
- Add `auto` system material mode and make it the default.
- Implement material with Avalonia `TransparencyLevelHint` only.
- Preserve settings page layout as direct `ScrollViewer -> StackPanel -> FASettingsExpander` content.
- Follow `docs/VISUAL_SPEC.md`, `docs/CORNER_RADIUS_SPEC.md`, and `docs/ai/SETTINGS_WINDOW_DESIGN.md`.

## Acceptance

- `dotnet build LanMountainDesktop.slnx -c Debug` succeeds.
- `dotnet test LanMountainDesktop.slnx -c Debug` succeeds or any unrelated failures are documented.
- The settings window can navigate by sidebar, titlebar Back, titlebar pane toggle, and search.
- Appearance settings expose Auto, None, Mica, and/or Acrylic according to system support.
- Existing dirty user changes are not reverted.
