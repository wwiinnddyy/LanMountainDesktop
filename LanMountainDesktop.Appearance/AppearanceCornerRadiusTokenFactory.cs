using Avalonia;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Shared.Contracts;

namespace LanMountainDesktop.Appearance;

public static class AppearanceCornerRadiusTokenFactory
{
    public static AppearanceCornerRadiusTokens Create(string style)
    {
        var normalized = GlobalAppearanceSettings.NormalizeCornerRadiusStyle(style);
        return normalized switch
        {
            GlobalAppearanceSettings.CornerRadiusStyleSharp => new AppearanceCornerRadiusTokens(
                Micro: new CornerRadius(4),
                Xs: new CornerRadius(8),
                Sm: new CornerRadius(10),
                Md: new CornerRadius(14),
                Lg: new CornerRadius(20),
                Xl: new CornerRadius(24),
                Island: new CornerRadius(28),
                Component: new CornerRadius(20)),
            GlobalAppearanceSettings.CornerRadiusStyleRounded => new AppearanceCornerRadiusTokens(
                Micro: new CornerRadius(8),
                Xs: new CornerRadius(14),
                Sm: new CornerRadius(16),
                Md: new CornerRadius(24),
                Lg: new CornerRadius(32),
                Xl: new CornerRadius(36),
                Island: new CornerRadius(40),
                Component: new CornerRadius(28)),
            GlobalAppearanceSettings.CornerRadiusStyleOpen => new AppearanceCornerRadiusTokens(
                Micro: new CornerRadius(10),
                Xs: new CornerRadius(16),
                Sm: new CornerRadius(20),
                Md: new CornerRadius(28),
                Lg: new CornerRadius(36),
                Xl: new CornerRadius(40),
                Island: new CornerRadius(44),
                Component: new CornerRadius(32)),
            // Balanced (default)
            _ => new AppearanceCornerRadiusTokens(
                Micro: new CornerRadius(6),
                Xs: new CornerRadius(12),
                Sm: new CornerRadius(14),
                Md: new CornerRadius(20),
                Lg: new CornerRadius(28),
                Xl: new CornerRadius(32),
                Island: new CornerRadius(36),
                Component: new CornerRadius(24))
        };
    }
}
