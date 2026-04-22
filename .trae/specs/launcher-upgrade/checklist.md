# Launcher Upgrade Checklist

- [x] Build passes for `LanMountainDesktop.Launcher`.
- [x] `update check` command returns structured JSON result.
- [x] `plugin update` command returns structured JSON result.
- [x] Legacy plugin install arguments still execute.
- [x] OOBE and splash are implemented as separate windows.
- [x] Update and rollback logic use version directory markers.

- [ ] Treat `first_run_completed` as legacy-only compatibility data.
- [ ] Keep the authoritative OOBE state in `%LOCALAPPDATA%\LanMountainDesktop\.launcher\state\oobe-state.json`.
