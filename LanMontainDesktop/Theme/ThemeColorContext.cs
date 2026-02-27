using Avalonia.Media;

namespace LanMontainDesktop.Theme;

public sealed record ThemeColorContext(
    Color AccentColor,
    bool IsLightBackground,
    bool IsLightNavBackground,
    bool IsNightMode);
