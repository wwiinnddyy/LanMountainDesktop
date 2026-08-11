namespace LanMountainDesktop.AirAppSdk;

public sealed record AirAppInstalledInfo(
    AirAppManifest Manifest,
    bool IsEnabled,
    bool IsLoaded,
    bool IsPackage,
    string? ErrorMessage);
