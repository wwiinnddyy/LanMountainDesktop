# LanMountainDesktop.PluginTemplate

Official `dotnet new` template package for LanMountainDesktop plugins.

## Baseline

- Target framework: `net10.0`
- Plugin SDK: `LanMountainDesktop.PluginSdk` `5.0.0`
- Manifest: `plugin.json`
- Package: `.laapp`
- Runtime mode: `in-proc`

## Install

```powershell
dotnet new install LanMountainDesktop.PluginTemplate
```

## Create a plugin

```powershell
dotnet new lmd-plugin -n YourPluginName
```

The generated project references `LanMountainDesktop.PluginSdk` and produces a `.laapp` package automatically when built.

## Package contract

Every plugin package must contain:

- `plugin.json`
- the entrance assembly declared by `entranceAssembly`
- the `.deps.json` next to the entrance assembly

Optional package content:

- `Localization/*.json`
- plugin assets and other managed dependencies
- `airappmarket-entry.template.json` in the repository root for market publishing

Market publishing uses `market-manifest.json` with `schemaVersion`, `manifest`, `compatibility`, `repository`, `publication.packageSources`, and `capabilities`.
