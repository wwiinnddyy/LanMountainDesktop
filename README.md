# LanMountainDesktop

`LanMountainDesktop` is the authoritative host repository for the desktop app and the host-side Plugin SDK.

## Repository Ownership

This repository owns:

- `LanMountainDesktop/`: desktop host app and plugin runtime
- `LanMountainDesktop.PluginSdk/`: canonical plugin API baseline (`4.0.0`)
- `LanMountainDesktop.Shared.Contracts/`: shared host/plugin contract types
- `LanMountainDesktop.Appearance/`: host appearance and radius token generation
- `LanMountainDesktop.Settings.Core/`: host settings primitives
- `LanMountainDesktop.Tests/`: host and SDK tests

This repository does not own:

- plugin market metadata or developer portal content
- official sample plugin release source
- independent ecosystem documentation hub

## Ecosystem Boundaries

- Host and SDK source of truth: `LanMountainDesktop` (this repo)
- Plugin market and developer materials: standalone `LanAirApp` repo
- Official sample plugin source of truth: standalone `LanMountainDesktop.SamplePlugin` repo
- `ClassIsland`: reference-only project, not part of build or release flow

## Plugin SDK v4 Baseline

- API baseline: `4.0.0`
- Manifest file: `plugin.json`
- Package extension: `.laapp`
- Entry model: `Initialize(HostBuilderContext, IServiceCollection)`
- Appearance model: `IPluginAppearanceContext`, `PluginAppearanceSnapshot`, `PluginCornerRadiusTokens`, `PluginCornerRadiusPreset`
- Component registration model: `AddPluginDesktopComponent<TControl>(PluginDesktopComponentOptions options)`

## Plugin Package Surfaces

- `LanMountainDesktop.PluginSdk`: official plugin SDK package (includes `buildTransitive` default `.laapp` packaging targets)
- `LanMountainDesktop.Shared.Contracts`: shared contract package for host/plugin boundaries
- `LanMountainDesktop.PluginTemplate`: official `dotnet new` template package (`shortName`: `lmd-plugin`)

Use `scripts/Pack-PluginPackages.ps1` to generate local-feed packages for CI or workspace integration tests.

## Workspace Market Resolution

For local market debugging, the host resolves workspace files from the sibling repository path (`..\\LanAirApp`) instead of reading the in-repo mirror folder.

See:

- `docs/ECOSYSTEM_BOUNDARIES.md`
- `docs/PLUGIN_SDK_V4_MIGRATION.md`
