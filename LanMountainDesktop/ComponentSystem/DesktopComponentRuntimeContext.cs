using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.ComponentSystem;

public sealed record DesktopComponentRuntimeContext(
    string ComponentId,
    string? PlacementId,
    ISettingsService SettingsService,
    IComponentSettingsAccessor ComponentSettingsAccessor);
