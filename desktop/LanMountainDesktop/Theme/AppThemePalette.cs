using Avalonia.Media;

namespace LanMountainDesktop.Theme;

public sealed record AppThemePalette(
    Color Primary,
    Color Secondary,
    Color Accent,
    Color OnAccent,
    Color AccentLight1,
    Color AccentLight2,
    Color AccentLight3,
    Color AccentDark1,
    Color AccentDark2,
    Color AccentDark3,
    Color SurfaceBase,
    Color SurfaceRaised,
    Color SurfaceOverlay,
    Color TextPrimary,
    Color TextSecondary,
    Color TextMuted,
    Color TextAccent,
    Color NavText,
    Color NavSelectedText,
    Color NavSelectionIndicator,
    Color NavItemBackground,
    Color NavItemHoverBackground,
    Color NavItemSelectedBackground,
    Color ToggleOn,
    Color ToggleOff,
    Color ToggleBorder);
