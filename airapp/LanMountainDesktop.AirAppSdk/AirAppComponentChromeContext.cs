using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Shared.Contracts;

namespace LanMountainDesktop.AirAppSdk;

public sealed record AirAppComponentChromeContext(
    string ComponentId,
    string? PlacementId,
    double CellSize,
    AppearanceCornerRadiusTokens CornerRadiusTokens,
    AirAppSettingsScope Scope = AirAppSettingsScope.App);
