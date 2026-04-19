# Checklist

- [x] `release.yml` produces signed FileMap incremental assets for Windows x64/x86 and Linux x64.
- [x] `release.yml` no longer depends on `vpk`/VeloPack packaging.
- [x] Launcher update engine applies only signed FileMap payload path.
- [x] Host update workflow no longer expects `releases.win.json`/`*.nupkg`.
- [x] Update source setting includes `pdc` and preserves GitHub fallback behavior.
- [ ] CI run attached proving all release matrix jobs pass.
- [ ] N-1 -> N incremental update verified on Windows x64/x86 and Linux x64.
- [ ] Rollback verification report attached.
