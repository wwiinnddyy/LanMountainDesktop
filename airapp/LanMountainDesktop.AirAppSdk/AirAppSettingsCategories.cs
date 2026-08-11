namespace LanMountainDesktop.AirAppSdk;

public static class AirAppSettingsCategories
{
    public const string General = "General";
    public const string Appearance = "Appearance";
    public const string Components = "Components";
    public const string AirApps = "AirApps";
    public const string AirAppCatalog = "AirAppCatalog";
    [Obsolete("Use AirAppCatalog instead.")]
    public const string AirAppMarket = AirAppCatalog;
    public const string Update = "Update";
    public const string About = "About";
    public const string Advanced = "Advanced";
    public const string External = "External";
}
