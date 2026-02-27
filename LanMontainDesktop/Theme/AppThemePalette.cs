using Avalonia.Media;

namespace LanMontainDesktop.Theme;

public sealed record AppThemePalette(
    Color TextPrimary,
    Color TextSecondary,
    Color TextMuted,
    Color TextAccent,
    Color NavText,
    Color NavSelectedText,
    Color NavItemBackground,
    Color NavItemHoverBackground,
    Color NavItemSelectedBackground);
