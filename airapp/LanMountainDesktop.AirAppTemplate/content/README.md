# __PLUGIN_NAME__

Official-style plugin scaffold generated for LanMountainDesktop.

## Build

```powershell
dotnet build -c Release
```

`LanMountainDesktop.PluginSdk` build targets will generate:

- plugin output files under `bin/<Configuration>/<TFM>/`
- a `.laapp` package in the project root

## Manifest

Update `plugin.json` fields as needed before release:

- `id`
- `name`
- `description`
- `author`
- `version`
- `runtime.mode` (`in-proc` by default, `isolated-background` for phase-1 worker mode)
