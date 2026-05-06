namespace LanMountainDesktop.PluginSdk;

public sealed record PluginMaterialSurfaceSnapshot(
    string BackgroundColor,
    string BorderColor,
    double BlurRadius,
    double Opacity);

public sealed record PluginAppearanceSnapshot(
    PluginCornerRadiusTokens CornerRadiusTokens,
    string ThemeVariant,
    string? AccentColor = null,
    string? SeedColor = null,
    string? ColorSource = null,
    string? SystemMaterialMode = null,
    IReadOnlyDictionary<string, string>? ColorRoles = null,
    IReadOnlyDictionary<string, PluginMaterialSurfaceSnapshot>? MaterialSurfaces = null,
    IReadOnlyList<string>? WallpaperSeedCandidates = null);
