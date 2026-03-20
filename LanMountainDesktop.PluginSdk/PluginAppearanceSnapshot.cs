namespace LanMountainDesktop.PluginSdk;

public sealed record PluginAppearanceSnapshot(
    double GlobalCornerRadiusScale,
    PluginCornerRadiusTokens CornerRadiusTokens,
    string ThemeVariant);
