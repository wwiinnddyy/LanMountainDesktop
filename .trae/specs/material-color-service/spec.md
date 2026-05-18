# Material Color Service

## Goal

Unify Monet seed extraction, wallpaper color extraction, semantic color roles, host material surfaces, and plugin appearance snapshots behind one host-owned material/color source of truth.

## Scope

- Host service: `IMaterialColorService`
- Compatibility facade: `IAppearanceThemeService`
- Settings page: `MaterialColorSettingsPage`
- Persisted settings:
  - `ThemeColorMode`
  - `ThemeColor`
  - `SelectedWallpaperSeed`
  - `SystemMaterialMode`
  - `ThemeWallpaperColorSource`
  - `UseNativeWallpaperChangeEvents`
  - `SystemWallpaperRefreshIntervalSeconds`
- Plugin read-only appearance snapshot fields:
  - accent color
  - seed color
  - color source
  - system material mode
  - semantic color roles
  - material surfaces
  - wallpaper seed candidates

## Behavior

`IMaterialColorService` owns the live `MaterialColorSnapshot`. Consumers should derive colors and material values from this snapshot instead of recalculating from raw theme settings, wallpaper settings, or `MonetPalette`.

Supported color sources:

- `default_neutral`: stable neutral surfaces with the default accent.
- `seed_monet`: user-selected seed color processed through Monet.
- `wallpaper_monet`: wallpaper colors processed through Monet.

Wallpaper color source selection:

- `auto`: app wallpaper or app solid color first, then system wallpaper, then fallback.
- `app`: app wallpaper or app solid color only, then fallback.
- `system`: system wallpaper only, then fallback.

System wallpaper monitoring:

- Native Windows user preference events are preferred when enabled and available.
- Polling remains active as the fallback path.
- Manual refresh clears cached wallpaper candidates and rebuilds the snapshot.

## Refactor Rules

- New consumers must depend on `IMaterialColorService`, not on parallel combinations of theme settings, wallpaper settings, and `MonetColorService`.
- `MonetColorService` remains the extraction/palette utility, not the application-wide coordinator.
- Component/editor/plugin appearance code must consume `MaterialColorSnapshot` or a mapper produced from it.
- Existing `IAppearanceThemeService` remains available for compatibility, but it must not become a second source of truth.

## Out Of Scope

- Plugin write access to global host appearance settings.
- Market metadata or sample plugin changes.
- Replacing the wallpaper picker page. It remains the asset/source management page.
