namespace LanMountainDesktop.PluginSdk;

/// <remarks>
/// This is the runtime snapshot shape consumed by plugins inside the host process.
/// It is intentionally distinct from the wire DTO with the same name in
/// <c>LanMountainDesktop.PluginIsolation.Contracts.PluginAppearanceSnapshot</c>.
/// </remarks>
public sealed record PluginMaterialSurfaceSnapshot(
    string BackgroundColor,
    string BorderColor,
    double BlurRadius,
    double Opacity);

/// <remarks>
/// Runtime-facing appearance snapshot for plugins. This is not the same contract as the
/// wire-format snapshot in <c>LanMountainDesktop.PluginIsolation.Contracts</c>, even though the
/// type name matches.
/// </remarks>
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
