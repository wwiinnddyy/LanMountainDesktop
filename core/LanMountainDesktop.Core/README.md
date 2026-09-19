# LanMountainDesktop.Core

Core shared library for the LanMountainDesktop host and AirApp ecosystem. This project merges the former `LanMountainDesktop.Shared.Contracts`, `LanMountainDesktop.Shared.IPC` and `LanMountainDesktop.PluginPackaging` packages.

## Includes

- **Shared.Contracts**: cross-boundary records used by the host, the AirApp runtime and AirApps; contract types for stable shared communication (update manifest, launcher IPC, privacy identity, deployment lock).
- **Shared.IPC**: public IPC abstractions and host/client helpers backed by `dotnetCampus.Ipc` (IPC client, public IPC host, AirApp runtime process starter/resolvers).
- **PluginPackaging**: `.laapp` AirApp package install/manifest utilities.

## Usage

```xml
<ItemGroup>
  <PackageReference Include="LanMountainDesktop.Core" Version="1.0.0" />
</ItemGroup>
```

> Note: AirApp authors should reference `LanMountainDesktop.AirAppSdk`, which depends on this package. This package is a transitive dependency and its types are not a stable author-facing API.
>
> The former `LanMountainDesktop.PluginSdk` (4.x / 5.x) is no longer supported: the host does not read `plugin.json` and does not load `IPlugin` / `PluginBase` assemblies. See `docs/AIRAPP_SDK_V1_MIGRATION.md` in the source repository.
