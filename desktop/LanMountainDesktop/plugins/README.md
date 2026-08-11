# Host Plugin Runtime

This directory contains the host-side plugin runtime for LanMountainDesktop.

## Responsibilities

- Discover, install, replace, and stage `.laapp` plugin packages
- Load plugin assemblies and shared contracts
- Integrate plugin settings sections, desktop components, and market UI
- Build plugin-scoped `IServiceCollection` / `ServiceProvider` for API `4.x` plugins
- Resolve shared contracts before activation and expose explicit plugin exports

## Relationship with LanAirApp

- `LanAirApp` is a standalone repository and owns market metadata plus developer ecosystem materials
- This host runtime only consumes market metadata and plugin packages
- The host no longer maintains an embedded `LanAirApp/` mirror inside this repository
- Workspace debugging resolves market files from sibling path `..\\LanAirApp\\...`

## Market Install Flow

1. Host reads the official market index
2. If both `releaseTag` and `releaseAssetName` are present, host resolves the exact GitHub Release asset first
3. If release resolution fails, host falls back to repository-root `.laapp`
4. Plugin detail text is read from plugin repository root `README.md`
5. Installation is staged and becomes effective after restart
