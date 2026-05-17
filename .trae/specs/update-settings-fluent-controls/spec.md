# Update Settings Fluent Controls

## Goal

Make the Settings > Update page the single user-facing control surface for the host update flow.

## Requirements

- The page uses Fluent Avalonia settings controls for update status, release facts, update behavior, and transfer controls.
- Users can choose update channel, download source, update mode, and download thread count.
- Update mode options are:
  - Manual: do not automatically download or install.
  - Silent Download: check and download in the background, then wait for user installation confirmation.
  - Silent Install: check and download in the background, then apply when the app exits.
- Users can opt into forced reinstall. When enabled, the update check targets the current version manifest where available and the UI labels the next payload as reinstall.
- The page displays whether the current payload is an incremental update or reinstall/full installer.
- The page exposes pause, resume, and cancel actions for resumable downloads and install recovery.
- Existing PloNDS/FileMap incremental update and Launcher rollback ownership remain unchanged.

## Acceptance

- `UpdateSettingsPage` shows Fluent Avalonia controls for channel, mode, thread count, forced reinstall, pause/resume, and cancel.
- `UpdateSettingsState` persists forced reinstall alongside other update preferences.
- Automatic startup checks skip manual mode, download in silent download/silent install modes, and leave installation to explicit user action or exit-time apply.
- Build succeeds for `LanMountainDesktop.slnx`.
