namespace LanMountainDesktop.AirAppIsolation.Contracts;

/// <summary>
/// Wire request for the IPC appearance snapshot payload. This request targets the
/// isolation-contract DTOs, not the runtime SDK snapshot with the same type name.
/// </summary>
public sealed record AirAppAppearanceSnapshotRequest(string SessionId);

public sealed record AirAppMaterialSurfaceSnapshot(
    string BackgroundColor,
    string BorderColor,
    double BlurRadius,
    double Opacity);

/// <summary>
/// Wire-format appearance snapshot exchanged over IPC.
/// Do not treat this as the same type as <c>LanMountainDesktop.AirAppSdk.AirAppAppearanceSnapshot</c>.
/// </summary>
public sealed record AirAppAppearanceSnapshot(
    string ThemeVariant,
    string? AccentColor = null,
    double CornerRadiusScale = 1.0,
    IReadOnlyDictionary<string, double>? CornerRadiusTokens = null,
    IReadOnlyDictionary<string, string>? ResourceAliases = null,
    string? SeedColor = null,
    string? ColorSource = null,
    string? SystemMaterialMode = null,
    IReadOnlyDictionary<string, string>? ColorRoles = null,
    IReadOnlyDictionary<string, AirAppMaterialSurfaceSnapshot>? MaterialSurfaces = null,
    IReadOnlyList<string>? WallpaperSeedCandidates = null);

/// <summary>
/// Wire notification carrying the IPC appearance snapshot.
/// </summary>
public sealed record AirAppAppearanceChangedNotification(AirAppAppearanceSnapshot Snapshot);
