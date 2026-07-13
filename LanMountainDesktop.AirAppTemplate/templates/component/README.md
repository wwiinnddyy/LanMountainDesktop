# LanMountainDesktop.AirApp.ComponentTemplate (Preview)

> [!IMPORTANT]
> This template is an API prototype. The production LanMountainDesktop Host, AirAppRuntime, and AirAppHost do not discover `airapp.json` or load assemblies produced by this template.

A prototype desktop-component AirApp project for exploring `LanMountainDesktop.AirAppSdk`.

## Build

```bash
dotnet build -c Release
```

This compiles the prototype project. The template project itself does not create a production-installable `.laapp` package.

## Install

There is currently no supported production install path for this output. Do not copy it to the plugins directory or submit it to the AirApp Market: production `.laapp` handling is the plugin package path and expects `plugin.json`, while this template uses `airapp.json`.

## Development

You can build the project to validate prototype API usage. An integrated preview/production loader is not implemented yet; `LanMountainDesktop.AirAppDevServer` also does not currently launch a real preview host.

## Customize

- Edit `MyWidget.cs` to modify the component UI and behavior
- Edit `airapp.json` to change metadata
- Add more components by creating additional widget classes and registering them in `MyAirApp.cs`
