# LanMountainDesktop.PluginSdk

Official SDK package for LanMountainDesktop plugins.

## Includes

- `IPlugin`/`PluginBase` entry abstractions
- `IPluginWorker`/`PluginWorkerBase` worker-side entry abstractions for isolated background mode
- `PluginManifest` and shared contract declarations
- `runtime.mode` manifest support for `in-proc`, `isolated-background`, and `isolated-window`
- desktop component registration extensions
- plugin runtime context and host service abstractions
- build-transitive packaging targets for `.laapp` output

## Quick Start

```xml
<ItemGroup>
  <PackageReference Include="LanMountainDesktop.PluginSdk" Version="5.0.0" />
</ItemGroup>
```

Create `plugin.json` in your plugin project root, then run `dotnet build` to produce both build output and a `.laapp` package.
