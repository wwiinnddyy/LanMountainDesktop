# VeloPack Update Integration (Deprecated)

## Status

This spec is deprecated and superseded by `.trae/specs/pdc-incremental-migration/`.

## Deprecation Reason

- VeloPack native package generation introduced unstable release blocking (version format coupling and platform divergence).
- The project has switched back to signed FileMap incremental assets as the primary update path.
- Launcher remains the update installer/rollback authority; packaging and distribution are being migrated to PDC/S3-compatible flows.

## Migration Note

Use `.trae/specs/pdc-incremental-migration/spec.md` as the active authority for incremental update implementation and acceptance.
