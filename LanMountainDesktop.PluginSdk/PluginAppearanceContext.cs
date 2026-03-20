namespace LanMountainDesktop.PluginSdk;

public sealed class PluginAppearanceContext : IPluginAppearanceContext
{
    public PluginAppearanceContext(PluginAppearanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.CornerRadiusTokens);

        Snapshot = snapshot with
        {
            GlobalCornerRadiusScale = Math.Max(0d, snapshot.GlobalCornerRadiusScale),
            ThemeVariant = string.IsNullOrWhiteSpace(snapshot.ThemeVariant)
                ? "Unknown"
                : snapshot.ThemeVariant.Trim()
        };
    }

    public PluginAppearanceSnapshot Snapshot { get; }

    public double ResolveScaledCornerRadius(double baseRadius, double? minimum = null, double? maximum = null)
    {
        var scale = Snapshot.GlobalCornerRadiusScale;
        var scaled = Math.Max(0d, baseRadius) * scale;
        var scaledMin = minimum.HasValue ? minimum.Value * scale : scaled;
        var scaledMax = maximum.HasValue ? maximum.Value * scale : scaled;
        return minimum.HasValue || maximum.HasValue
            ? Math.Clamp(scaled, scaledMin, scaledMax)
            : scaled;
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
