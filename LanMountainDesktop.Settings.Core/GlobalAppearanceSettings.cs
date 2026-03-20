namespace LanMountainDesktop.Settings.Core;

public static class GlobalAppearanceSettings
{
    public const double DefaultCornerRadiusScale = 1.0;
    public const double MinimumCornerRadiusScale = 0.0;
    public const double MaximumCornerRadiusScale = 2.50;

    public static double NormalizeCornerRadiusScale(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultCornerRadiusScale;
        }

        return Math.Clamp(value, MinimumCornerRadiusScale, MaximumCornerRadiusScale);
    }
}
