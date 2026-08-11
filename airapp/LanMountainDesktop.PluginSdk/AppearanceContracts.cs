namespace LanMountainDesktop.PluginIsolation.Contracts;

/// <summary>
/// Wire request for the IPC appearance snapshot payload. This request targets the
/// isolation-contract DTOs, not the runtime SDK snapshot with the same type name.
/// </summary>
public sealed record PluginAppearanceSnapshotRequest(string SessionId);

public sealed record PluginMaterialSurfaceSnapshot(
    string BackgroundColor,
    string BorderColor,
    double BlurRadius,
    double Opacity);

/// <summary>
/// Wire-format appearance snapshot exchanged over IPC.
/// Do not treat this as the same type as <c>LanMountainDesktop.PluginSdk.PluginAppearanceSnapshot</c>.
/// </summary>
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

/// <summary>
/// Wire notification carrying the IPC appearance snapshot.
/// </summary>
public sealed record PluginAppearanceChangedNotification(PluginAppearanceSnapshot Snapshot);
