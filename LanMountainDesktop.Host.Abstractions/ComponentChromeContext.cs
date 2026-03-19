using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Shared.Contracts;

namespace LanMountainDesktop.Host.Abstractions;

public sealed record ComponentChromeContext(
    string ComponentId,
    string? PlacementId,
    double CellSize,
    double GlobalCornerRadiusScale,
    AppearanceCornerRadiusTokens CornerRadiusTokens,
    SettingsScope Scope = SettingsScope.App);
