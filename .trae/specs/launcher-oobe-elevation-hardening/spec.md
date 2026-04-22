# Launcher OOBE and Elevation Hardening Spec

## Goal

Stabilize the launcher startup path so that:

- OOBE does not reappear for the same Windows user after reinstall/upgrade.
- Normal startup, OOBE, update checks, incremental downloads, and default plugin installs do not trigger unexpected UAC prompts.
- Only the approved elevation paths remain allowed.

## Scope

- Launcher OOBE state handling
- launch source classification
- elevation boundary cleanup
- plugin install default behavior
- diagnostic logging and troubleshooting guidance

## Behavior

- OOBE state is stored as a per-user truth source at `%LOCALAPPDATA%\LanMountainDesktop\.launcher\state\oobe-state.json`.
- `first_run_completed` is treated as a legacy compatibility marker only.
- `launchSource` values are treated as:
  - `normal`
  - `postinstall`
  - `apply-update`
  - `plugin-install`
  - `debug-preview`
- Automatic OOBE is allowed only for normal user-mode startup.
- `postinstall` may show OOBE only when the launcher is not elevated and user state is available.
- `apply-update`, `plugin-install`, and `debug-preview` must not auto-enter OOBE.
- Allowed elevation paths are limited to:
  - the installer itself
  - full installer update application
  - user-confirmed legacy uninstall
- Default plugin installation targets the current user's LocalAppData scope and must not request elevation by default.

## Acceptance

- Same-user reinstall does not re-enter OOBE.
- Missing or damaged OOBE state does not silently bounce the user back into OOBE loops.
- Default plugin installation path never triggers surprise UAC.
- Logs can explain why OOBE was shown or suppressed and why elevation was or was not requested.
