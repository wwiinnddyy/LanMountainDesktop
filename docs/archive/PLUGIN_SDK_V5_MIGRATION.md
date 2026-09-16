# Plugin SDK v5 Migration Guide

> **⚠️ 已废弃 / DEPRECATED**
>
> 本文档描述的是已停止支持的 `LanMountainDesktop.PluginSdk`（`plugin.json` + `IPlugin`）。
> 宿主自 AirApp SDK 1.0.0 起不再识别 `plugin.json`，也不会加载基于 `IPlugin` / `PluginBase` 的程序集。
> 阑山桌面现在只有一个 SDK：`LanMountainDesktop.AirAppSdk`，同时覆盖桌面组件与窗口轻应用。
>
> 迁移请看 [AirApp SDK 迁移指南](../AIRAPP_SDK_V1_MIGRATION.md)；
> 新开发请看 [轻应用开发指南](../01-AirApp开发/README.md)。
> 本文仅作历史归档保留。

Plugin SDK v5 is the Avalonia 12 compatibility baseline for LanMountainDesktop plugins.

## What Changed

- Rebuild plugins against `LanMountainDesktop.PluginSdk` `5.0.0`.
- Set `plugin.json` `apiVersion` to `5.0.0`.
- Target `net10.0` and use Avalonia `12.0.1` compatible UI dependencies.
- Use `FluentAvaloniaUI` `3.0.0-preview1` and `FluentIcons.Avalonia` `2.1.325` when a plugin directly references those packages.

## Compatibility

SDK v5 is a binary breaking change because the SDK exposes Avalonia UI types such as `Control`, `UserControl`, and `SettingsPageBase`. Plugins built for SDK v4 must be rebuilt and republished for SDK v5.

The host does not provide an Avalonia 11 / Avalonia 12 dual UI stack. The public extension entry points remain the same: custom settings pages still derive from `SettingsPageBase`, and desktop components still provide Avalonia controls through the existing registration APIs.

## Appearance Snapshot

`IPluginAppearanceContext.Snapshot` remains read-only. In addition to theme variant and corner radius tokens, the snapshot can now include host material/color data:

- `AccentColor`
- `SeedColor`
- `ColorSource`
- `SystemMaterialMode`
- `ColorRoles`
- `MaterialSurfaces`
- `WallpaperSeedCandidates`

Existing plugins that only read `CornerRadiusTokens` and `ThemeVariant` continue to work. New plugins should treat the added properties as optional and prefer `ColorRoles`/`MaterialSurfaces` over hard-coded colors.

## Minimal Package Update

```xml
<ItemGroup>
  <PackageReference Include="LanMountainDesktop.PluginSdk" Version="5.0.0" />
</ItemGroup>
```

```json
{
  "apiVersion": "5.0.0"
}
```

## Validation

After updating package versions and rebuilding the plugin, verify that the generated `.laapp` contains the rebuilt assembly, `plugin.json`, and `.deps.json` next to the plugin entry assembly.
