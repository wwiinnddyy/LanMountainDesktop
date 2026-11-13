# Settings Window Fluent Shell Design

This document is the authoritative implementation note for the LanMountainDesktop settings window shell.
General visual tokens still come from `docs/archive/VISUAL_SPEC.md` and `docs/archive/CORNER_RADIUS_SPEC.md`
（归档文档，仅作历史对照；当前的键名与数值以 `docs/03-组件设计规范/00-设计token真源.md` 为准）。

## References

- Current host settings implementation in `LanMountainDesktop/Views/SettingsWindow.axaml`.
- ClassIsland `SettingsWindowNew`: titlebar navigation buttons, titlebar pane toggle, `NavigationView` width, right-side drawer.
- SecRandom v3 Avalonia `SettingsView`: titlebar search, restart action, `NavigationView` compact toggle, search result highlight.
- Awesome Design / Fluent style notes: quiet app surface, token-driven spacing, system material as backdrop instead of decorative panels.

## Shell

- The settings window remains an independent top-level window opened through `SettingsWindowService`.
- The shell uses a 48 DIP custom titlebar and one `FANavigationView` as the main container.
- The titlebar left cluster is: Back, pane toggle, app/settings icon, window title.
- The titlebar center is a settings `AutoCompleteBox` search field.
- The titlebar right cluster is: restart prompt, more options, Windows caption-button spacer.
- The fallback pane toggle belongs in the titlebar, not the navigation footer.
- Content remains unframed: pages render directly in the `FAFrame`; drawers are the only side panel.

## Navigation And Search

- `FANavigationView.OpenPaneLength` stays near 283 DIP and may scale within the existing responsive limits.
- Navigation history is local to the settings window; using Back does not close the window or affect the desktop shell.
- Search entries always include page-level descriptors.
- Built-in pages are also scanned for `FASettingsExpander` and `FASettingsExpanderItem` text.
- Selecting a search result navigates to its page, expands parent settings expanders, scrolls/focuses the target, and shows a short accent highlight.
- Plugin and generated pages are searchable at page level unless their controls are already loaded and can be scanned.

## System Material

- `SystemMaterialMode` supports `auto`, `none`, `mica`, and `acrylic`.
- The default is `auto`.
- The implementation uses Avalonia `Window.TransparencyLevelHint`; it does not use WinUI SDK interop or private platform accessors.
- Auto mode uses this priority:
  - Windows 11: `Mica`, then `AcrylicBlur`, then `Blur`, then `None`.
  - Windows 10: `AcrylicBlur`, then `Blur`, then `None`.
  - Other systems or disabled transparency: `None`.
- The settings-window root brush remains translucent for material modes so it does not cover the OS backdrop.

## Layout Rules

- Settings pages use `ScrollViewer -> StackPanel.settings-page-container -> FASettingsExpander`.
- Avoid nested surface cards inside the settings content area.
- Use dynamic design tokens for radius and colors.
- Widget root radius rules still follow `DesignCornerRadiusComponent`; settings shell internals use the smaller design radius tokens.
