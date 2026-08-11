using LanMountainDesktop.AirApps;
using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.Services;

public sealed record AirAppSettingsSectionContribution(
    LoadedAirApp AirApp,
    AirAppSettingsSectionRegistration Registration);

public sealed record AirAppDesktopComponentContribution(
    LoadedAirApp AirApp,
    AirAppComponentRegistration Registration);

public sealed record AirAppDesktopComponentEditorContribution(
    LoadedAirApp AirApp,
    AirAppComponentEditorRegistration Registration);
