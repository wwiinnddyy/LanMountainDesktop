# LanMountainDesktop.PluginTemplate

Official `dotnet new` template package for LanMountainDesktop plugins.

## Install

```powershell
dotnet new install LanMountainDesktop.PluginTemplate
```

## Create a plugin

```powershell
dotnet new lmd-plugin -n YourPluginName
```

The generated project references `LanMountainDesktop.PluginSdk` and produces a `.laapp` package automatically when built.
