namespace LanMountainDesktop.PluginSdk;

public enum SettingsPageCategory
{
    General = 0,
    Appearance = 10,
    Components = 20,
    Plugins = 30,
    PluginCatalog = 35,
    [Obsolete("Use PluginCatalog instead.")]
    PluginMarket = 35,
    About = 40
}
