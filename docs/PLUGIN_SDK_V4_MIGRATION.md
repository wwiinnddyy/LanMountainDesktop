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

## Corner Radius System

Plugin widgets must follow the host's corner radius settings to maintain visual consistency with built-in components.

### Why Plugins Cannot Use XAML Resources

Plugins run in a separate `AssemblyLoadContext` and cannot directly access the host's resource dictionary. Therefore, `{DynamicResource DesignCornerRadiusComponent}` is not available in plugin XAML. Instead, plugins must resolve corner radius values in code through `PluginDesktopComponentContext`.

### Available Corner Radius Presets

| Preset | Default Value | Usage |
|--------|---------------|-------|
| `Micro` | 6px | Tiny elements |
| `Xs` | 12px | Small elements and icon containers |
| `Sm` | 14px | Small colored blocks |
| `Md` | 20px | Common buttons/cards |
| `Lg` | 28px | Normal glass panels |
| `Xl` | 32px | Emphasized containers |
| `Island` | 36px | Large containers |
| `Component` | 18px | **Desktop widget standard radius** |
| `Default` | (adaptive) | Adaptive based on cell size |

### Corner Radius API Reference

```csharp
public class MyWidget : Border
{
    public MyWidget(PluginDesktopComponentContext context)
    {
        // Method 1: Use preset tokens (recommended for consistency)
        CornerRadius = context.ResolveCornerRadius(PluginCornerRadiusPreset.Component);
        
        // Method 2: Use preset with fallback (extension method)
        CornerRadius = context.Appearance.Snapshot.ResolveCornerRadius(
            PluginCornerRadiusPreset.Md, 
            fallback: new CornerRadius(8));
        
        // Method 3: Custom radius with global scale applied
        CornerRadius = context.ResolveScaledCornerRadius(baseRadius: 16, minimum: 8, maximum: 24);
        
        // Method 4: Access tokens directly
        var tokens = context.CornerRadiusTokens;
        CornerRadius = tokens.ToCornerRadius(PluginCornerRadiusPreset.Md);
        
        // Method 5: Get raw token value (double)
        double componentRadius = context.CornerRadiusTokens.Component;
    }
}
```

### Best Practices

1. **Always use `PluginCornerRadiusPreset.Component` for the widget root container** - This ensures consistency with built-in widgets.

2. **Apply corner radius in code, not XAML** - Since plugins cannot access host resources, set `CornerRadius` in the constructor or code-behind.

3. **Re-apply radius on size changes** - For adaptive layouts, subscribe to `SizeChanged` and recalculate:

```csharp
public MyWidget(PluginDesktopComponentContext context)
{
    _context = context;
    ApplyCornerRadius();
    SizeChanged += (_, _) => ApplyCornerRadius();
}

private void ApplyCornerRadius()
{
    var basis = Math.Min(Bounds.Width, Bounds.Height);
    CornerRadius = _context.ResolveCornerRadius(
        PluginCornerRadiusPreset.Component,
        minimum: Math.Clamp(basis * 0.08, 8, 16),
        maximum: Math.Clamp(basis * 0.15, 16, 28));
}
```

4. **Inner elements can use smaller presets** - For cards or buttons inside your widget:

```csharp
var innerCard = new Border
{
    CornerRadius = _context.ResolveCornerRadius(PluginCornerRadiusPreset.Md)
};
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
