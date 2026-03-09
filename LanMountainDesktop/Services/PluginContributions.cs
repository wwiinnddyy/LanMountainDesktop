using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

public sealed record PluginSettingsPageContribution(
    LoadedPlugin Plugin,
    PluginSettingsPageRegistration Registration);

public sealed record PluginDesktopComponentContribution(
    LoadedPlugin Plugin,
    PluginDesktopComponentRegistration Registration);
