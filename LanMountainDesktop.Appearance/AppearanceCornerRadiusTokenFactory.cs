using Avalonia;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Shared.Contracts;

namespace LanMountainDesktop.Appearance;

public static class AppearanceCornerRadiusTokenFactory
{
    public static AppearanceCornerRadiusTokens Create(double scale)
    {
        var normalizedScale = GlobalAppearanceSettings.NormalizeCornerRadiusScale(scale);
        return new AppearanceCornerRadiusTokens(
            Radius(6, normalizedScale),
            Radius(10, normalizedScale),
            Radius(14, normalizedScale),
            Radius(18, normalizedScale),
            Radius(24, normalizedScale),
            Radius(30, normalizedScale),
            Radius(36, normalizedScale));
    }

    private static CornerRadius Radius(double value, double scale)
    {
        var scaled = Math.Round(value * scale * 2, MidpointRounding.AwayFromZero) / 2d;
        return new CornerRadius(scaled);
    }
}
