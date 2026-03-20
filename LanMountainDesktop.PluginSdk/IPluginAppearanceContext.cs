namespace LanMountainDesktop.PluginSdk;

public interface IPluginAppearanceContext
{
    PluginAppearanceSnapshot Snapshot { get; }

    double ResolveScaledCornerRadius(double baseRadius, double? minimum = null, double? maximum = null);

    double ResolveCornerRadius(PluginCornerRadiusPreset preset, double? minimum = null, double? maximum = null);
}
