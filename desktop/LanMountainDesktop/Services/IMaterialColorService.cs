using System;
using Avalonia.Controls;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

public interface IMaterialColorService
{
    MaterialColorSnapshot GetMaterialColorSnapshot();

    MaterialColorSnapshot BuildMaterialColorPreview(ThemeAppearanceSettingsState pendingState);

    event EventHandler<MaterialColorSnapshot>? MaterialColorChanged;

    void ApplyThemeResources(IResourceDictionary resources);

    MaterialSurfaceSnapshot GetSurface(MaterialSurfaceRole role);

    void ApplyWindowMaterial(Window window, MaterialSurfaceRole role);

    void RefreshWallpaperColors();
}
