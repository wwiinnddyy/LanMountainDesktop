# LanMountainDesktop.AirAppSdk

Official AirApp (Lightweight Application) SDK for LanMountainDesktop.

AirApp is the unified extension system for LanMountainDesktop. Developers use this SDK to build
**desktop components** (rendered in the host process on the desktop grid) and **window AirApps**
(hosted in the isolated `AirAppHost` process).

## Includes

- `IAirApp` / `AirAppBase` entry abstractions with `[AirAppEntrance]`
- `IAirAppWorker` / `AirAppWorkerBase` worker-side entry abstractions for isolated background mode
- `AirAppManifest` (`airapp.json`) and shared contract declarations
- `runtime.mode` manifest support for `in-process`, `isolated-background`, and `isolated-window`
- Desktop component registration (`AddAirAppComponent<TControl>`) and window registration (`AddAirAppWindow<TWindow>`)
- Declarative and custom settings sections (`AddAirAppSettingsSection`)
- Runtime context, appearance, settings, message bus, logger and host service abstractions
- Build-transitive packaging targets for `.laapp` output

## Quick Start

Install the template and create an AirApp project:

```bash
dotnet new install LanMountainDesktop.AirAppTemplate
dotnet new lmd-airapp -n MyAirApp
```

Or reference the package directly:

```xml
<ItemGroup>
  <PackageReference Include="LanMountainDesktop.AirAppSdk" Version="1.0.0" />
</ItemGroup>
```

Create `airapp.json` in your AirApp project root, then run `dotnet build` to produce both build
output and a `.laapp` package. Install the `.laapp` package into LanMountainDesktop to load your
AirApp.
