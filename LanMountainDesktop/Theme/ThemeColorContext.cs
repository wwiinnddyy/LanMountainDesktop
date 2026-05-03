using System.Collections.Generic;
using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Theme;

public sealed record ThemeColorContext(
    Color AccentColor,
    bool IsLightBackground,
    bool IsLightNavBackground,
    bool IsNightMode,
    MonetPalette? MonetPalette = null,
    IReadOnlyList<Color>? MonetColors = null,
    bool UseNeutralSurfaces = false,
    string SystemMaterialMode = ThemeAppearanceValues.MaterialAuto);
