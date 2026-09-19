# Host AirApp Runtime

This directory contains the host-side AirApp runtime for LanMountainDesktop.

## Responsibilities

- Discover, install, replace, and stage `.laapp` AirApp packages
- Load AirApp assemblies and shared contracts
- Integrate AirApp settings sections, desktop components, and market UI
- Build AirApp-scoped `IServiceCollection` / `ServiceProvider` for API `1.x` AirApps
- Resolve shared contracts before activation and expose explicit AirApp exports

The entry contract is `IAirApp` marked with `[AirAppEntrance]`; `airapp.json` is the only
manifest the loader reads. `apiVersion` major must equal `AirAppSdkInfo.ApiVersion`
(currently `1.0.0`), otherwise the load is rejected.

## Relationship with LanAirApp

- `LanAirApp` is a standalone repository and owns market metadata plus developer ecosystem materials
- This host runtime only consumes market metadata and AirApp packages
- The host no longer maintains an embedded `LanAirApp/` mirror inside this repository
- Workspace debugging resolves market files from sibling path `..\LanAirApp\...`

## Market Install Flow

1. Host reads the official market index
2. If both `releaseTag` and `releaseAssetName` are present, host resolves the exact GitHub Release asset first
3. If release resolution fails, host falls back to repository-root `.laapp`
4. AirApp detail text is read from the AirApp repository root `README.md`
5. Installation is staged and becomes effective after restart

## Legacy identifiers kept on purpose

Some identifiers in this area still read `plugin` although the concept is now AirApp. They are
frozen, not missed during cleanup — renaming them breaks installed data or an out-of-repo contract:

- Market index fields `plugins` / `pluginId` are the wire format shared with `LanAirApp`
  (`AirAppMarketModels.cs` binds them onto `AirApp*` types via `JsonPropertyName`).
- The settings section id `"plugins"` is **not** persisted (nothing stores the selected page/section),
  so it can be renamed — but it is matched in pairs: `SettingsCatalogService.cs:23` defines it and
  `Views/SettingsPages/AirAppsSettingsPage.axaml.cs:8` consumes it via `[AirAppSettingsPageInfo]`.
  Renaming only one side silently detaches the page, which neither the build nor the tests catch.
- `airapp-settings.json` used to be named `plugin-settings.json`; the rename happens on first load
  via `AppDataPathProvider.TryMoveLegacyPath`, which never merges or deletes.
- `LANMOUNTAIN_PLUGIN_*` environment variables and `--plugin-id` are the host-to-runtime
  process protocol; they need dual-read support rather than a hard rename.

The deprecated `LanMountainDesktop.PluginSdk` (4.x / 5.x) is not supported here: this loader never
reads `plugin.json` and never loads `IPlugin` / `PluginBase` assemblies.
