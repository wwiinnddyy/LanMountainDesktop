using System.Collections.Generic;
using Avalonia.Media;

namespace LanMountainDesktop.Theme;

public sealed record ThemeColorContext(
    Color AccentColor,
    bool IsLightBackground,
    bool IsLightNavBackground,
    bool IsNightMode,
    IReadOnlyList<Color>? MonetColors = null);
