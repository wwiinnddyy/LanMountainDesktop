namespace LanMountainDesktop.PluginSdk;

public sealed class PluginAppearanceContext : IPluginAppearanceContext
{
    public PluginAppearanceContext(PluginAppearanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.CornerRadiusTokens);

        Snapshot = snapshot with
        {
            ThemeVariant = string.IsNullOrWhiteSpace(snapshot.ThemeVariant)
                ? "Unknown"
                : snapshot.ThemeVariant.Trim()
        };
    }

    public PluginAppearanceSnapshot Snapshot { get; }

    public double ResolveScaledCornerRadius(double baseRadius, double? minimum = null, double? maximum = null)
    {
        var value = Math.Max(0d, baseRadius);
        if (!minimum.HasValue && !maximum.HasValue)
        {
            return value;
        }

        var clampedMin = minimum ?? value;
        var clampedMax = maximum ?? value;
        return Math.Clamp(value, clampedMin, clampedMax);
    }

    public double ResolveCornerRadius(PluginCornerRadiusPreset preset, double? minimum = null, double? maximum = null)
    {
        var resolved = Math.Max(0d, Snapshot.CornerRadiusTokens.Get(preset));
        if (!minimum.HasValue && !maximum.HasValue)
        {
            return resolved;
        }

        var clampedMin = minimum ?? resolved;
        var clampedMax = maximum ?? resolved;
        if (clampedMin > clampedMax)
        {
            (clampedMin, clampedMax) = (clampedMax, clampedMin);
        }

        return Math.Clamp(resolved, clampedMin, clampedMax);
    }
}
