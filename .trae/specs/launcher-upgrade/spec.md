# Launcher Upgrade Spec

## Goal

Upgrade `LanMountainDesktop.Launcher` into the unified Launcher for:

- OOBE first-run entry
- startup splash window
- silent/incremental/rollback update
- plugin install/update

## Scope (Phase 1)

- Avalonia GUI launcher with two windows:
  - `OOBEWindow` (first run only)
  - `SplashWindow` (every launch)
- Default command `launch`
- CLI commands:
  - `update check|download|apply|rollback`
  - `plugin install|update`
- Legacy compatibility:
  - `--source --plugins-dir --result` still works for plugin install

## Update Behavior

- ClassIsland-style deployment folders:
  - `app-<version>-<number>/`
  - marker files `.current`, `.partial`, `.destroy`
- Signed file map:
  - `files.json`
  - `files.json.sig`
  - `public-key.pem`
- Incremental update:
  - `replace` from archive
  - `reuse` from current deployment
  - `delete` skip file in target deployment
- Rollback:
  - snapshot metadata is written before apply
  - automatic rollback on apply failure
  - manual rollback via command

## OOBE and Splash

- OOBE is independent from splash.
- OOBE shows only:
  - welcome text: `欢迎使用阑山桌面`
  - arrow button for continue
- Splash shows only:
  - app name: `阑山桌面`

## Extensibility

- `IOobeStep` for future multi-step OOBE
- `ISplashStageReporter` for future startup progress visualization

## Compatibility Addendum

- The current production OOBE state format is a per-user JSON file at `%LOCALAPPDATA%\LanMountainDesktop\.launcher\state\oobe-state.json`.
- `first_run_completed` remains legacy compatibility data only.
- Same-user reinstall or upgrade should not re-enter OOBE.
