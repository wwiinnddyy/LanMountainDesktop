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
        return Clamped(context.ResolveCornerRadius(preset));
    }

    public static CornerRadius ResolveCornerRadius(
        this IAirAppAppearanceContext context,
        AirAppCornerRadiusPreset preset,
        double minimum,
        double maximum)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Clamped(context.ResolveCornerRadius(preset, minimum, maximum));
    }

    public static CornerRadius ResolveScaledCornerRadius(
        this IAirAppAppearanceContext context,
        double baseRadius)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Clamped(context.ResolveScaledCornerRadius(baseRadius));
    }

    public static CornerRadius ResolveScaledCornerRadius(
        this IAirAppAppearanceContext context,
        double baseRadius,
        double minimum,
        double maximum)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Clamped(context.ResolveScaledCornerRadius(baseRadius, minimum, maximum));
    }

    public static CornerRadius ResolveCornerRadius(
        this AirAppComponentContext context,
        AirAppCornerRadiusPreset preset,
        double minimum,
        double maximum)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Clamped(context.ResolveCornerRadius(preset, minimum, maximum));
    }

    /// <summary>
    /// "半径不能是负的"这条钳位规则只有一处。此前五个公开方法各写一遍
    /// <c>new CornerRadius(Math.Max(0d, value))</c>——改口径要数五处，漏一处的症状是
    /// 某一档圆角在缩放后变成负数而被主题层拒掉。
    /// </summary>
    private static CornerRadius Clamped(double value) => new(Math.Max(0d, value));

    public static AirAppAppearanceSnapshot GetAppearanceSnapshot(
        this AirAppComponentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Appearance.Snapshot;
    }
}
