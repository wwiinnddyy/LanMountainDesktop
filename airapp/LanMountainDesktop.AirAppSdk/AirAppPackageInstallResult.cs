namespace LanMountainDesktop.AirAppSdk;

public sealed record AirAppPackageInstallResult(
    AirAppManifest Manifest,
    bool ReplacedExisting,
    bool RestartRequired);
