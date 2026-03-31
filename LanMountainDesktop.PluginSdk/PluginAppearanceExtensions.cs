using Avalonia;

namespace LanMountainDesktop.PluginSdk;

public static class PluginAppearanceExtensions
{
    public static CornerRadius ResolveCornerRadius(
        this PluginAppearanceSnapshot snapshot,
        PluginCornerRadiusPreset preset)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var value = snapshot.CornerRadiusTokens.Get(preset);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static CornerRadius ResolveCornerRadius(
        this PluginAppearanceSnapshot snapshot,
        PluginCornerRadiusPreset preset,
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
        this IPluginAppearanceContext context,
        PluginCornerRadiusPreset preset)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.ResolveCornerRadius(preset);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static CornerRadius ResolveCornerRadius(
        this IPluginAppearanceContext context,
        PluginCornerRadiusPreset preset,
        double minimum,
        double maximum)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.ResolveCornerRadius(preset, minimum, maximum);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static CornerRadius ResolveScaledCornerRadius(
        this IPluginAppearanceContext context,
        double baseRadius)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.ResolveScaledCornerRadius(baseRadius);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static CornerRadius ResolveScaledCornerRadius(
        this IPluginAppearanceContext context,
        double baseRadius,
        double minimum,
        double maximum)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.ResolveScaledCornerRadius(baseRadius, minimum, maximum);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static CornerRadius ResolveCornerRadius(
        this PluginDesktopComponentContext context,
        PluginCornerRadiusPreset preset,
        double minimum,
        double maximum)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.ResolveCornerRadius(preset, minimum, maximum);
        return new CornerRadius(Math.Max(0d, value));
    }

    public static PluginAppearanceSnapshot GetAppearanceSnapshot(
        this PluginDesktopComponentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Appearance.Snapshot;
    }
}
