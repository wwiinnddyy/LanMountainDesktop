namespace LanMountainDesktop.PluginSdk;

public interface IPluginPackageManager
{
    IReadOnlyList<InstalledPluginInfo> GetInstalledPlugins();

    PluginPackageInstallResult InstallPackage(string packagePath);
}
