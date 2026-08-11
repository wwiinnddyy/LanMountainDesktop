namespace LanMountainDesktop.AirAppSdk;

public interface IAirAppPackageManager
{
    IReadOnlyList<AirAppInstalledInfo> GetInstalledAirApps();

    AirAppPackageInstallResult InstallPackage(string packagePath);
}
