namespace LanMountainDesktop.PluginPackaging;

public sealed class PluginPackageInstallOptions
{
    public bool IncludeLegacyPackages { get; init; }

    public static PluginPackageInstallOptions Default { get; } = new();

    public static PluginPackageInstallOptions WithLegacySupport { get; } = new() { IncludeLegacyPackages = true };
}
