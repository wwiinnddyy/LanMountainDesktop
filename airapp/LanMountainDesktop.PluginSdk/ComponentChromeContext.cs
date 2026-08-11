using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Shared.Contracts;

namespace LanMountainDesktop.Host.Abstractions;

public sealed record ComponentChromeContext(
    string ComponentId,
    string? PlacementId,
    double CellSize,
    AppearanceCornerRadiusTokens CornerRadiusTokens,
    SettingsScope Scope = SettingsScope.App);
