# Checklist

- [x] `release.yml` does not invoke Velopack.
- [x] `plonds-build.yml` uploads app payload artifacts and generates PloNDS delta/static outputs.
- [x] S3 output path is rooted at `lanmountain/update/` (no system version prefix).
- [x] CI workflow expects `repo/`, `meta/`, `manifests/`, and `installers/` outputs after a release run.
- [x] Host update source keeps compatibility (`pdc`/`stcn` normalize to active PloNDS source).
- [x] Host can persist PloNDS payload into launcher incoming directory.
- [x] Launcher can apply PloNDS FileMap payload with signature/hash verification.
- [x] Legacy signed `files.json + update.zip` path still works as compatibility fallback.
- [x] Launcher keeps rollback-capable deployments after successful update.
- [x] Manual rollback returns a structured failure when the snapshot source directory is missing.
- [ ] CI run attached proving all release matrix jobs pass.
- [x] N-1 -> N incremental update verified locally on Windows x64.
- [ ] N-1 -> N incremental update verified on Windows x86 and Linux x64.
- [x] Rollback regression tests attached in `LanMountainDesktop.Tests`.
