namespace LanMountainDesktop.AirAppPackaging;

public sealed record AirAppPackageInstallResult(string InstalledPackagePath, AirAppPackageManifest Manifest);
