# LanMountainDesktop.PluginSdk

Official SDK package for LanMountainDesktop plugins.

## Includes

- `IPlugin`/`PluginBase` entry abstractions
- `PluginManifest` and shared contract declarations
- desktop component registration extensions
- plugin runtime context and host service abstractions
- build-transitive packaging targets for `.laapp` output

## Quick Start

```xml
<ItemGroup>
  <PackageReference Include="LanMountainDesktop.PluginSdk" Version="4.0.1" />
</ItemGroup>
```

Create `plugin.json` in your plugin project root, then run `dotnet build` to produce both build output and a `.laapp` package.
