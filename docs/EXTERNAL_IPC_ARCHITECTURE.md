# External IPC Architecture

## Scope

This document defines the current external integration IPC baseline for LanMountainDesktop.

- The delivery focus is external application integration, not plugin process isolation.
- `dotnetCampus.Ipc` is the single IPC foundation for Host public APIs, Launcher/OOBE startup notifications, and plugin-contributed external services.
- Process isolation remains a future track and stays documented in `docs/PLUGIN_PROCESS_ISOLATION_ARCHITECTURE.md`.

## Design Summary

The public IPC stack is split into two complementary layers:

1. Strongly typed public services
   - Contracts are marked with `[IpcPublic]`.
   - Host exposes service instances through `CreateIpcJoint<TContract>(instance)`.
   - .NET clients connect once and obtain strong typed proxies through `CreateIpcProxy<TContract>(peer)`.
2. Routed notifications
   - `JsonIpcDirectRoutedProvider.NotifyAsync` is used for one-way event delivery.
   - Startup progress, loading-state updates, catalog changed events, and plugin live events all use routed notify IDs.

This keeps command/query calls explicit and strongly typed while still giving plugins and Launcher a lightweight event channel.

## Projects

- `LanMountainDesktop.Shared.IPC`
  - Public IPC constants, routed notify IDs, DTOs, strong-typed public service contracts, host/client helpers, and DI registration helpers.
- `LanMountainDesktop`
  - Runs `PublicIpcHostService`, exposes built-in public services, and folds plugin-contributed services into one external catalog.
- `LanMountainDesktop.Launcher`
  - Connects to the Host public pipe and listens for startup and loading-state notifications instead of running a custom length-prefixed IPC server.
- `LanMountainDesktop.PluginSdk`
  - Adds `IPluginPublicIpcContributor`, `IPluginPublicIpcBuilder`, and `AddPluginPublicIpc(...)`.

## Built-in Public Services

Current built-in `[IpcPublic]` contracts:

- `IPublicAppInfoService`
  - Returns application metadata such as version, codename, process id, pipe name, and startup time.
- `IPublicShellControlService`
  - Allows external .NET clients to activate the shell, open settings, request restart, and request exit.
- `IPublicPluginCatalogService`
  - Returns the merged public IPC catalog snapshot exposed by Host.

## Routed Notify IDs

Current fixed routed notify IDs:

- `lanmountain.catalog.changed`
- `lanmountain.launcher.startup-progress`
- `lanmountain.launcher.loading-state`

The fixed routed surface is intentionally small. Runtime variation happens in the service catalog and in plugin-contributed service instances, not in ad-hoc top-level route registration after startup.

## Host Lifecycle

`PublicIpcHostService` is started during Host application startup and remains the single external IPC entry point.

Responsibilities:

- Start a named `dotnetCampus.Ipc` provider.
- Register fixed request routes before `StartServer()`.
- Expose built-in strong-typed public services.
- Maintain the merged service catalog.
- Publish startup and loading-state notifications to connected clients.
- Accept plugin-contributed public services after plugin load.

## Launcher / OOBE Migration

Launcher no longer depends on the previous custom named-pipe length-prefixed protocol as the primary path.

- Host publishes `StartupProgressMessage` through `lanmountain.launcher.startup-progress`.
- Host publishes `LoadingStateMessage` through `lanmountain.launcher.loading-state`.
- Launcher connects as a normal public IPC client and subscribes to those routed notifications.

This means Splash/OOBE is now just another IPC consumer on the same base transport used by external integrators.

## Plugin Public IPC Contribution Model

Plugins can contribute new external IPC services in two ways:

1. Declarative registration
   - `services.AddPluginPublicIpc<TContract, TImplementation>(...)`
2. Advanced contributor
   - Register `IPluginPublicIpcContributor`
   - Use `IPluginPublicIpcBuilder` to contribute services from plugin DI

At plugin load time the Host runtime:

- discovers `PluginPublicIpcServiceRegistration`
- executes `IPluginPublicIpcContributor`
- validates that contributed contracts are `[IpcPublic]` interfaces
- registers the resolved instances into `PublicIpcHostService`
- emits `lanmountain.catalog.changed`

Plugins can also inject `IExternalIpcNotificationPublisher` and translate internal DI/message-bus events into routed notifications such as:

- `lanmountain.plugin.{pluginId}.attendance.updated`
- `lanmountain.plugin.{pluginId}.status.changed`

## Service Catalog

The public catalog is represented by `PublicIpcCatalogSnapshot` and includes:

- built-in and plugin-provided public services
- contract type metadata
- optional object id
- owning `pluginId` for plugin services
- declared notify IDs
- current loaded/enabled plugin list

This catalog is available through:

- strong-typed public service `IPublicPluginCatalogService`
- fixed request route `lanmountain.catalog.get`
- routed notify `lanmountain.catalog.changed`

## Current Limitations

- Strong-typed proxy/joint support is .NET-first.
- Plugin service removal is still restart-bound. New services can be added at runtime, but service removal is not yet modeled as a live unload contract.
- Cross-language clients still need a .NET bridge or sidecar if they want to consume `[IpcPublic]` contracts directly.
- Plugin process isolation is not part of this delivery. That remains future work.
