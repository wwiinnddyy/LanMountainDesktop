using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.Services;

public enum AirAppCatalogSourceKind
{
    Package = 0,
    Manifest = 1,
    DevAirApp = 2
}

public sealed record AirAppCatalogEntry(
    AirAppManifest Manifest,
    string SourcePath,
    bool IsPackage,
    bool IsEnabled,
    bool IsLoaded,
    string? ErrorMessage,
    int SettingsPageCount,
    int WidgetCount,
    bool IsDevAirApp = false);
