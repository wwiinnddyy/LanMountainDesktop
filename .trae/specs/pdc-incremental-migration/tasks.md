# Tasks

- [x] Remove VeloPack packaging from release workflow.
- [x] Keep signed FileMap path as interim compatibility fallback.
- [x] Remove launcher/runtime Velopack branching.
- [ ] Add `phainon.yml` for PDCC publish configuration.
- [ ] Add PDCC installation + publish steps in `release.yml`.
- [ ] Upload app payload artifacts for PDCC consumption in release build jobs.
- [ ] Publish PDC metadata + object repo to S3 path root `lanmountain/update/`.
- [ ] Mirror installers to `lanmountain/update/installers/<platform>/<arch>/`.
- [ ] Replace update source canonical value with `stcn` (keep legacy `pdc` compatibility).
- [ ] Add PDC payload model into host update check result.
- [ ] Add host download path for PDC payload (`pdc-filemap.json` + signature + metadata).
- [ ] Add launcher PDC FileMap apply path with rollback-compatible semantics.
- [ ] Keep old `files.json + update.zip` path behind compatibility fallback.
