namespace LanMountainDesktop.PluginSdk;

public sealed class PluginLoaderOptions
{
    public string ManifestFileName { get; init; } = PluginSdkInfo.ManifestFileName;

    public string PackageFileExtension { get; init; } = PluginSdkInfo.PackageFileExtension;

    public string DataDirectoryName { get; init; } = PluginSdkInfo.DataDirectoryName;

    public string RuntimeDirectoryName { get; init; } = PluginSdkInfo.RuntimeDirectoryName;

    public string ExtractedPackagesDirectoryName { get; init; } = PluginSdkInfo.ExtractedPackagesDirectoryName;

    public string PackagedDataDirectoryName { get; init; } = PluginSdkInfo.PackagedDataDirectoryName;

    public ISet<string> SharedAssemblyNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        typeof(IPlugin).Assembly.GetName().Name!
    };
}
