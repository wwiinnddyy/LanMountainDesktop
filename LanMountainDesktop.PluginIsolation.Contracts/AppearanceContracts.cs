namespace LanMountainDesktop.PluginIsolation.Contracts;

public sealed record PluginAppearanceSnapshotRequest(string SessionId);

public sealed record PluginMaterialSurfaceSnapshot(
    string BackgroundColor,
    string BorderColor,
    double BlurRadius,
    double Opacity);

public sealed record PluginAppearanceSnapshot(
    string ThemeVariant,
    string? AccentColor = null,
    double CornerRadiusScale = 1.0,
    IReadOnlyDictionary<string, double>? CornerRadiusTokens = null,
    IReadOnlyDictionary<string, string>? ResourceAliases = null,
    string? SeedColor = null,
    string? ColorSource = null,
    string? SystemMaterialMode = null,
    IReadOnlyDictionary<string, string>? ColorRoles = null,
    IReadOnlyDictionary<string, PluginMaterialSurfaceSnapshot>? MaterialSurfaces = null,
    IReadOnlyList<string>? WallpaperSeedCandidates = null);

public sealed record PluginAppearanceChangedNotification(PluginAppearanceSnapshot Snapshot);
