using System;
using System.Collections.Generic;

using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.AirApps;

public sealed class AirAppLoaderOptions
{
    public string ManifestFileName { get; init; } = AirAppSdkInfo.ManifestFileName;

    public string PackageFileExtension { get; init; } = AirAppSdkInfo.PackageFileExtension;

    public string DataDirectoryName { get; init; } = AirAppSdkInfo.DataDirectoryName;

    public string RuntimeDirectoryName { get; init; } = AirAppSdkInfo.RuntimeDirectoryName;

    public string ExtractedPackagesDirectoryName { get; init; } = AirAppSdkInfo.ExtractedPackagesDirectoryName;

    public string PackagedDataDirectoryName { get; init; } = AirAppSdkInfo.PackagedDataDirectoryName;

    public bool IsDevMode { get; init; }

    public ISet<string> SharedAssemblyNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        typeof(IAirApp).Assembly.GetName().Name!
    };
}
