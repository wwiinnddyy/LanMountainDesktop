# PDC Incremental Update Migration

## Goal

Replace VeloPack-based incremental packaging with a unified PDC FileMap + object-repo pipeline, while keeping Launcher installation, rollback, and update orchestration ownership unchanged.

## Stage 1 (Completed)

- Release workflow removed VeloPack-based release packaging.
- Signed FileMap path was restored as an interim release mechanism.
- Host/Launcher fallback behavior stayed compatible with `files.json + files.json.sig + update.zip`.

## Stage 2 (Current Implementation Target)

- Use GitHub Actions PloNDS static publishing as the active incremental path.
- Keep `phainon.yml` for future PDCC parity, but do not rely on PDCC for the current release flow.
- Promote PloNDS-distributed FileMap/object-repo as the primary incremental path.
- Keep GitHub Release installers and metadata as parallel distribution.
- Keep Launcher state machine ownership (`.current/.partial/.destroy` + snapshots).
- Check updates in order: NS3/PloNDS static source, GitHub Release PloNDS assets, then GitHub full installer.
- S3 object root is fixed to `lanmountain/update/` with no update-system version prefix.
- Public object URLs come from `S3_PUBLIC_BASE_URL`; do not infer them from `S3_ENDPOINT` and `S3_BUCKET`.

Expected S3 layout:
  - `lanmountain/update/repo/sha256/<hash-prefix>/<hash-object>`
  - `lanmountain/update/meta/channels/<channel>/<platform>/latest.json`
  - `lanmountain/update/meta/distributions/<distributionId>.json`
  - `lanmountain/update/manifests/<distributionId>/plonds-filemap.json`
  - `lanmountain/update/manifests/<distributionId>/plonds-filemap.json.sig`
  - `lanmountain/update/installers/<platform>/<version>/*`

## Acceptance

- `release.yml` contains no Velopack steps; PloNDS static publishing is handled by `plonds-build.yml` and `ddss-publish.yml`.
- Release jobs keep building installers for Windows x64/x86, Linux x64, and macOS.
- PloNDS metadata + FileMap + object repo are published under `lanmountain/update/`.
- Host can consume the NS3/PloNDS static payload and fallback to GitHub when unavailable.
- Launcher can apply both:
  - legacy signed `files.json + update.zip`
  - PloNDS FileMap object-repo payload.
- Rollback semantics keep both automatic failure rollback and manual rollback after a successful update.

## Deprecated Notes

- The following interim outputs are compatibility-only (not the long-term primary path):
  - `files-windows-x64.json` / `.sig` / `update-windows-x64.zip`
  - `files-windows-x86.json` / `.sig` / `update-windows-x86.zip`
  - `files-linux-x64.json` / `.sig` / `update-linux-x64.zip`
