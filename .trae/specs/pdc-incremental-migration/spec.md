# PDC Incremental Update Migration

## Goal

Replace VeloPack-based incremental packaging with a unified PDC FileMap + object-repo pipeline, while keeping Launcher installation, rollback, and update orchestration ownership unchanged.

## Stage 1 (Completed)

- Release workflow removed VeloPack-based release packaging.
- Signed FileMap path was restored as an interim release mechanism.
- Host/Launcher fallback behavior stayed compatible with `files.json + files.json.sig + update.zip`.

## Stage 2 (Current Implementation Target)

- Move release publishing to PDCC + `phainon.yml` (ClassIsland-style).
- Promote PDC-distributed FileMap/object-repo as the primary incremental path.
- Keep GitHub Release installers and metadata as parallel distribution.
- Keep Launcher state machine ownership (`.current/.partial/.destroy` + snapshots).
- Update source defaults to `stcn` (S3/PDC), with GitHub fallback.
- S3 object root is fixed to `lanmountain/update/` with no update-system version prefix.

Expected S3 layout:
  - `lanmountain/update/repo/<hash-prefix>/<hash-object>`
  - `lanmountain/update/meta/channels/<channel>/<subchannel>/latest.json`
  - `lanmountain/update/meta/distributions/<distributionId>/*.json`
  - `lanmountain/update/installers/<platform>/<arch>/*`

## Acceptance

- `release.yml` includes PDCC publish steps and no Velopack steps.
- Release jobs keep building installers for Windows x64/x86, Linux x64, and macOS.
- PDC metadata + FileMap + object repo are published under `lanmountain/update/`.
- Host can consume PDC payload (`stcn` source) and fallback to GitHub when unavailable.
- Launcher can apply both:
  - legacy signed `files.json + update.zip`
  - PDC FileMap object-repo payload.
- Rollback semantics remain unchanged.

## Deprecated Notes

- The following interim outputs are compatibility-only (not the long-term primary path):
  - `files-windows-x64.json` / `.sig` / `update-windows-x64.zip`
  - `files-windows-x86.json` / `.sig` / `update-windows-x86.zip`
  - `files-linux-x64.json` / `.sig` / `update-linux-x64.zip`
