# Checklist

- [ ] `release.yml` includes PDCC publish flow and does not invoke Velopack.
- [ ] `release.yml` uploads app payload artifacts for PDCC.
- [ ] S3 output path is rooted at `lanmountain/update/` (no system version prefix).
- [ ] S3 has `repo/`, `meta/`, and `installers/` outputs after a release run.
- [ ] Host update source default is `stcn` and old `pdc` values are auto-normalized.
- [ ] Host can persist PDC payload into launcher incoming directory.
- [ ] Launcher can apply PDC FileMap payload with signature/hash verification.
- [ ] Legacy signed `files.json + update.zip` path still works as compatibility fallback.
- [ ] CI run attached proving all release matrix jobs pass.
- [ ] N-1 -> N incremental update verified on Windows x64/x86 and Linux x64.
- [ ] Rollback verification report attached.
