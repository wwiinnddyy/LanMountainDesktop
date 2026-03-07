using LanMountainDesktop.Services;

namespace LanMountainDesktop.ComponentSystem;

public sealed record DesktopComponentRuntimeContext(
    string ComponentId,
    string? PlacementId,
    IComponentInstanceSettingsStore ComponentSettingsStore);
