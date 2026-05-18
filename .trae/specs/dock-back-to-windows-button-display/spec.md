# Dock Back To Windows Button Display

## Summary

The Dock "Back to platform" action should expose a configurable left icon slot while keeping the localized platform text fixed.

## Requirements

- The default display mode is `IconAndText` so existing users keep a familiar Dock layout after upgrade.
- The localized platform text remains controlled by the app and is not user-editable.
- General > Basic Settings exposes one Fluent Avalonia `FASettingsExpander` for the back-to-platform button, with icon-related controls folded into nested `FASettingsExpanderItem` rows.
- The main row exposes a dropdown with `IconAndText`, `IconOnly`, and `TextOnly` options.
- A nested icon source row selects Fluent icon or text icon.
- Fluent icon mode uses a popup picker-style flyout with search and a grid of the full FluentIcons `Icon` enum.
- Text icon mode lets the user enter short text for the left icon slot.
- Changing the dropdown persists to `AppSettingsSnapshot.BackToWindowsButtonDisplayMode` and updates the Dock button without restarting.
- Changing the icon source, Fluent icon, or text icon persists to app settings and updates the Dock button without restarting.
- `IconOnly` keeps the existing tooltip text so the button remains understandable.
- `PinnedTaskbarActions` continues to control whether the action is visible; it does not replace the display mode setting.

## Acceptance Scenarios

- With default settings, the Dock button shows a small circle icon and the localized platform text.
- Selecting icon only hides the platform text and keeps the configured left icon visible.
- Selecting text only hides the left icon slot and keeps the localized platform text visible.
- Choosing a Fluent icon changes the left icon slot.
- Entering a short text icon changes the left icon slot.
- Restarting the app restores the selected display mode.
- Clicking the button still runs the existing minimize/back-to-platform behavior.
