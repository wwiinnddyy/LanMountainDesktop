using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ComponentSystem;

public sealed record DesktopComponentRuntimeContext(
    string ComponentId,
    string? PlacementId,
    ISettingsFacadeService SettingsFacade,
    ISettingsService SettingsService,
    IAppearanceThemeService AppearanceTheme,
    AirAppComponentChromeContext Chrome,
    IComponentSettingsAccessor ComponentSettingsAccessor,
    IComponentInstanceSettingsStore ComponentSettingsStore,
    DesktopComponentRenderMode RenderMode = DesktopComponentRenderMode.Live);
