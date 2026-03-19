namespace LanMountainDesktop.Settings.Core;

public static class GlobalAppearanceSettings
{
    public const double DefaultCornerRadiusScale = 1.0;
    public const double MinimumCornerRadiusScale = 0.70;
    public const double MaximumCornerRadiusScale = 1.40;
    public const double CornerRadiusScaleStep = 0.05;

    public static double NormalizeCornerRadiusScale(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultCornerRadiusScale;
        }

        var clamped = Math.Clamp(value, MinimumCornerRadiusScale, MaximumCornerRadiusScale);
        return Math.Round(clamped / CornerRadiusScaleStep, MidpointRounding.AwayFromZero) * CornerRadiusScaleStep;
    }
}
