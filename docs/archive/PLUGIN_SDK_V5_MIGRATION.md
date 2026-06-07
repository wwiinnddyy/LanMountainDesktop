# Plugin SDK v5 Migration Guide

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
