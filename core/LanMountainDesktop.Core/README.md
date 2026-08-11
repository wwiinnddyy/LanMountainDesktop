# LanMountainDesktop.Core

Core shared library for the LanMountainDesktop host and plugin ecosystems. This project merges the former `LanMountainDesktop.Shared.Contracts`, `LanMountainDesktop.Shared.IPC` and `LanMountainDesktop.PluginPackaging` packages.

## Includes

- **Shared.Contracts**: cross-boundary records used by host/runtime and plugins; contract types for stable shared communication (update manifest, launcher IPC, privacy identity, deployment lock).
- **Shared.IPC**: public IPC abstractions and host/client helpers backed by `dotnetCampus.Ipc` (IPC client, public IPC host, AirApp runtime process starter/resolvers).
- **PluginPackaging**: `.laapp` plugin package install/manifest utilities.

## Usage

```xml
<ItemGroup>
  <PackageReference Include="LanMountainDesktop.Core" Version="6.0.0" />
</ItemGroup>
```

> Note: v6.0.0 replaces the previous `LanMountainDesktop.Shared.Contracts` and `LanMountainDesktop.Shared.IPC` package ids. Plugin consumers should reference `LanMountainDesktop.PluginSdk`, which depends on this package.
