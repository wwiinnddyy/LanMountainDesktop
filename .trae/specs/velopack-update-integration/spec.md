# VeloPack Update Integration

## Goal
Switch incremental package generation and release assets to VeloPack native outputs while keeping Launcher as the update installer and rollback authority.

## Requirements
- CI/release pipeline produces `releases.win.json` and `*.nupkg` assets for Windows x64.
- Launcher can detect pending VeloPack payload in `.launcher/update/incoming`.
- Launcher applies update into new `app-*` deployment and preserves rollback snapshot behavior.
- Existing launcher responsibilities (OOBE/startup/plugin upgrade) remain unchanged.

## Acceptance
- Build and quality workflows pass after migration changes.
- Release workflow publishes VeloPack assets.
- Launcher `update apply` succeeds with VeloPack full package payload.
- Manual rollback still works after a VeloPack-based update.
