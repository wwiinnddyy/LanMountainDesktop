namespace LanMountainDesktop.PluginSdk;

public static class SettingsCategories
{
    public const string General = "General";
    public const string Appearance = "Appearance";
    public const string Components = "Components";
    public const string Plugins = "Plugins";
    public const string PluginCatalog = "PluginCatalog";
    [Obsolete("Use PluginCatalog instead.")]
    public const string PluginMarket = PluginCatalog;
    public const string Update = "Update";
    public const string About = "About";
    public const string Advanced = "Advanced";
    public const string External = "External";
}
