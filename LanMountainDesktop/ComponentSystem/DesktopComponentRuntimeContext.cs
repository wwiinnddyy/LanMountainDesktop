using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ComponentSystem;

public sealed record DesktopComponentRuntimeContext(
    string ComponentId,
    string? PlacementId,
    ISettingsFacadeService SettingsFacade,
    ISettingsService SettingsService,
    IAppearanceThemeService AppearanceTheme,
    IComponentSettingsAccessor ComponentSettingsAccessor,
    IComponentInstanceSettingsStore ComponentSettingsStore);
