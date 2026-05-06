# Material Color Service Acceptance Checklist

- [x] `dotnet build LanMountainDesktop.slnx -c Debug` succeeds.
- [x] `dotnet test LanMountainDesktop.slnx -c Debug` succeeds.
- [x] Material & Color page exposes color source, wallpaper source, system material, native event preference, polling interval, manual refresh, semantic color preview, and surface preview.
- [x] Appearance page no longer owns duplicate visible color/material controls.
- [x] Appearance page view model preserves Material & Color settings instead of rewriting them.
- [x] Component corner-radius settings preserve Material & Color fields instead of resetting them through old positional constructors.
- [x] Component editor receives colors from `MaterialColorSnapshot`.
- [x] Plugin SDK snapshot includes read-only color/material fields without breaking the existing constructor shape.
- [x] Wallpaper source selection supports auto, app, and system modes.
- [x] Native wallpaper event monitoring can be disabled and polling remains available.
