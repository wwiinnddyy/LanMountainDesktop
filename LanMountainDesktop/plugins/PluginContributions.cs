using LanMountainDesktop.Plugins;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

public sealed record PluginSettingsSectionContribution(
    LoadedPlugin Plugin,
    PluginSettingsSectionRegistration Registration);

public sealed record PluginDesktopComponentContribution(
    LoadedPlugin Plugin,
    PluginDesktopComponentRegistration Registration);
