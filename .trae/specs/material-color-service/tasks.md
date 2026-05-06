# Material Color Service Tasks

- [x] Add unified material/color snapshot models and `IMaterialColorService`.
- [x] Persist wallpaper color source and native wallpaper event preference.
- [x] Add the Material & Color settings page.
- [x] Keep Appearance focused on theme mode, window chrome, and corner radius.
- [x] Route plugin appearance snapshots through the material/color snapshot.
- [x] Route component editor theming through the material/color snapshot.
- [x] Remove legacy color/material preview and save logic from the Appearance page view model.
- [x] Replace legacy positional `ThemeAppearanceSettingsState` writes with preserving `with` updates where found.
- [x] Keep native wallpaper events optional with polling/manual refresh fallback.
- [x] Add regression tests for normalization, plugin mapping, and component editor palette mapping.
- [ ] Continue retiring legacy direct consumers of raw theme/wallpaper/Monet tuples when they are touched.
