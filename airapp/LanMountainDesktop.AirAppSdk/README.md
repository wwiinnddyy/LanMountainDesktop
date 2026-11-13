# LanMountainDesktop.AirAppSdk

Official AirApp (Lightweight Application) SDK for LanMountainDesktop.

AirApp is the unified extension system for LanMountainDesktop. Developers use this SDK to build
**desktop components** (rendered in the host process on the desktop grid) and **window AirApps**
(hosted in the isolated `AirAppHost` process).

## Includes

- `IAirApp` / `AirAppBase` entry abstractions with `[AirAppEntrance]`
- `AirAppManifest` (`airapp.json`) and shared contract declarations
- `runtime.mode` manifest field. `in-process` is the only mode the host actually executes;
  `isolated-background` and `isolated-window` are accepted by the manifest validator but do not
  change how the AirApp is loaded today.
- Desktop component registration (`AddAirAppComponent<TControl>`) and window registration (`AddAirAppWindow<TWindow>`)
- Declarative and custom settings sections (`AddAirAppSettingsSection`)
- Runtime context, appearance, settings, message bus, logger and host service abstractions
- Build-transitive packaging targets for `.laapp` output

> Peer scope: the SDK carries contracts and defaults only. AirApps must reference it with
> `ExcludeAssets="runtime;native"` `PrivateAssets="all"` — the host supplies these assemblies at
> runtime, and a `.laapp` that ships its own copy fails to load with duplicate-type errors.

## Quick Start

Install the template and create an AirApp project:

```bash
dotnet new install LanMountainDesktop.AirAppTemplate
dotnet new lmd-airapp -n MyAirApp
```

Or reference the package directly (use the version published for your host's API major):

```xml
<ItemGroup>
  <PackageReference Include="LanMountainDesktop.AirAppSdk" Version="1.0.1"
                    ExcludeAssets="runtime;native" PrivateAssets="all" />
</ItemGroup>
```

Create `airapp.json` in your AirApp project root, then run `dotnet build` to produce both build
output and a `.laapp` package. Install the `.laapp` package into LanMountainDesktop to load your
AirApp.
