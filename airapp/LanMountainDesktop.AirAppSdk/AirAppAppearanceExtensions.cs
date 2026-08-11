using Avalonia;

namespace LanMountainDesktop.AirAppSdk;

public static class AirAppAppearanceExtensions
{
    public static CornerRadius ResolveCornerRadius(
        this AirAppAppearanceSnapshot snapshot,
        AirAppCornerRadiusPreset preset)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var value = snapshot.CornerRadiusTokens.Get(preset);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static CornerRadius ResolveCornerRadius(
        this AirAppAppearanceSnapshot snapshot,
        AirAppCornerRadiusPreset preset,
        CornerRadius fallback)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var value = snapshot.CornerRadiusTokens.Get(preset);
        if (!double.IsFinite(value) || value < 0)
        {
            return fallback;
        }
        return new CornerRadius(value);
    }

    public static CornerRadius ResolveCornerRadius(
        this IAirAppAppearanceContext context,
        AirAppCornerRadiusPreset preset)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.ResolveCornerRadius(preset);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static CornerRadius ResolveCornerRadius(
        this IAirAppAppearanceContext context,
        AirAppCornerRadiusPreset preset,
        double minimum,
        double maximum)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.ResolveCornerRadius(preset, minimum, maximum);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static CornerRadius ResolveScaledCornerRadius(
        this IAirAppAppearanceContext context,
        double baseRadius)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.ResolveScaledCornerRadius(baseRadius);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static CornerRadius ResolveScaledCornerRadius(
        this IAirAppAppearanceContext context,
        double baseRadius,
        double minimum,
        double maximum)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.ResolveScaledCornerRadius(baseRadius, minimum, maximum);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static CornerRadius ResolveCornerRadius(
        this AirAppComponentContext context,
        AirAppCornerRadiusPreset preset,
        double minimum,
        double maximum)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.ResolveCornerRadius(preset, minimum, maximum);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static AirAppAppearanceSnapshot GetAppearanceSnapshot(
        this AirAppComponentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Appearance.Snapshot;
    }
}
