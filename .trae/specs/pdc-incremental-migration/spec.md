# PDC Incremental Update Migration

## Goal

Replace VeloPack-based incremental packaging with a unified signed FileMap pipeline and prepare for PDC/S3 distribution compatibility, while keeping Launcher installation, rollback, and update orchestration ownership unchanged.

## Stage 1 (Completed in this round)

- Release workflow outputs signed FileMap incremental assets as the primary path:
  - `files-windows-x64.json` / `.sig` / `update-windows-x64.zip`
  - `files-windows-x86.json` / `.sig` / `update-windows-x86.zip`
  - `files-linux-x64.json` / `.sig` / `update-linux-x64.zip`
- Launcher and host update runtime remove VeloPack branches and return to signed FileMap apply path.
- Host update asset discovery supports platform-scoped names with fallback to legacy generic names.
- Optional S3 sync publishes incremental assets in parallel with GitHub Release assets.

## Stage 2 (In Progress)

- Introduce PDC-compatible update source (`pdc`) with fallback to GitHub.
- Add PDC metadata/latest/distribution API consumption abstraction.
- Keep Launcher install/apply/rollback state machine unchanged.
- Prepare `phainon.yml`-compatible release metadata for future PDCC integration.

## Acceptance

- `release.yml` no longer contains VeloPack packaging steps.
- Windows x64/x86 and Linux x64 release jobs all upload signed FileMap incremental assets.
- Host auto-update can detect and download platform-matching signed FileMap assets.
- Launcher `update apply` succeeds with signed FileMap payload and rollback behavior remains unchanged.
- Optional S3 upload step works when S3 secrets/vars are configured.
