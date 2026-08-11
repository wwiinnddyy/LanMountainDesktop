namespace LanMountainDesktop.AirAppSdk;

public enum AirAppSettingsPageCategory
{
    General = 0,
    Appearance = 10,
    Components = 20,
    AirApps = 30,
    AirAppCatalog = 35,
    [Obsolete("Use AirAppCatalog instead.")]
    AirAppMarket = 35,
    About = 40,
    Dev = 50
}
