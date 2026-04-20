# PLONDS Skeleton

Penguin Logistics Online Network Distribution System, or PLONDS, is the standalone update-distribution skeleton for LanMountainDesktop.

This directory is intentionally isolated from the main app and Launcher. It contains only the new distribution protocol, a thin read-only API, and sample S3-style metadata files.

## Directory Layout

```text
PenguinLogisticsOnlineNetworkDistributionSystem/
  README.md
  src/
    Plonds.Shared/
    Plonds.Api/
  sample-data/
    meta/
      channels/
        stable/
          windows-x64/
          windows-x86/
          linux-x64/
      distributions/
```

## Projects

- `Plonds.Shared` provides protocol constants and models.
- `Plonds.Core` owns hashing, diffing, object-repo generation, manifest generation, signing, and publish orchestration.
- `Plonds.Tool` is the CI-facing CLI entrypoint. PowerShell should stay as a thin wrapper around this tool.
- `Plonds.Api` is a thin read-only API that reads metadata from a local folder laid out like S3.

## Architecture

PLONDS is intentionally built around a single C# implementation stack so the protocol and publish behavior do not drift across languages.

```text
Host App
  -> checks updates, downloads objects, stages incoming payload
Launcher
  -> verifies signature, applies file map, switches deployment, rolls back

PLONDS.Api
  -> read-only metadata projection for clients
PLONDS.Tool
  -> CI/release command surface
PLONDS.Core
  -> hash/diff/object-repo/sign/publish implementation
PLONDS.Shared
  -> protocol constants and DTOs
```

Rules for v1:

- Core protocol behavior should live in `Plonds.Core`, not in PowerShell scripts.
- `scripts/*.ps1` may remain only as thin wrappers for GitHub Actions and local convenience.
- Host keeps download responsibility.
- Launcher keeps apply, atomic switch, snapshot, and rollback responsibility.

## Storage Layout

The first version keeps one fixed object root:

```text
lanmountain/update/
  repo/sha256/<prefix>/<hash>
  meta/channels/<channel>/<platform>/latest.json
  meta/distributions/<distributionId>.json
  installers/<platform>/<version>/...
```

Planned but not enabled in v1:

```text
lanmountain/update/repo-compressed/<algo>/<prefix>/<hash>
lanmountain/update/patches/<algo>/<baseHash>/<targetHash>
```

## Public Endpoints

The API base path is `/api/plonds/v1`.

- `GET /healthz`
- `GET /api/plonds/v1/metadata`
- `GET /api/plonds/v1/channels/{channel}/{platform}/latest?currentVersion=...`
- `GET /api/plonds/v1/distributions/{distributionId}`

## Local Run

```powershell
dotnet run --project src/Plonds.Api
```

By default the API reads metadata from `sample-data`.
