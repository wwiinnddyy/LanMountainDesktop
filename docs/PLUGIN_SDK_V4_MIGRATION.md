# Plugin SDK v4 Migration Guide

This guide describes the breaking changes introduced by Plugin SDK `4.0.0`.

## Version Baseline

- Host plugin SDK baseline: `4.0.0`
- Plugins targeting `3.x` are rejected by default
- Manifest file remains `plugin.json`

## Breaking Changes

1. `AddPluginDesktopComponent` now uses options-first registration.
2. `PluginDesktopComponentOptions` is now the canonical component registration shape and must include `ComponentId`.
3. Appearance and radius access are provided through strongly typed APIs:
   - `IPluginAppearanceContext`
   - `PluginAppearanceSnapshot`
   - `PluginCornerRadiusTokens`
   - `PluginCornerRadiusPreset`
4. `PluginDesktopComponentContext` now exposes `Appearance` as the primary appearance access point.

## New Component Registration Pattern

```csharp
services.AddPluginDesktopComponent<MyWidget>(new PluginDesktopComponentOptions
{
    ComponentId = "YourPlugin.Widget",
    DisplayName = "My Widget",
    IconKey = "PuzzlePiece",
    Category = "Plugins",
    MinWidthCells = 4,
    MinHeightCells = 3,
    CornerRadiusPreset = PluginCornerRadiusPreset.Default
});
```

## Appearance Usage Pattern

```csharp
public MyWidget(PluginDesktopComponentContext context)
{
    var mdRadius = context.Appearance.ResolveCornerRadius(PluginCornerRadiusPreset.Md);
    var adaptiveRadius = context.Appearance.ResolveScaledCornerRadius(12, 8, 20);
}
```

## Manifest Update

Update plugin manifests to API `4.x`:

```json
{
  "apiVersion": "4.0.0"
}
```

## Validation Checklist

- `plugin.json` declares `apiVersion` `4.0.0` (or compatible `4.x`)
- component registration migrated to options model
- runtime appearance access uses `IPluginAppearanceContext`
- plugin package rebuilt and republished as `.laapp`
