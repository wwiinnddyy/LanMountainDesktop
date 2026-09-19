namespace LanMountainDesktop.AirAppPackaging;

public sealed class AirAppPackageInstallOptions
{
    public bool IncludeLegacyPackages { get; init; }

    public static AirAppPackageInstallOptions Default { get; } = new();

    public static AirAppPackageInstallOptions WithLegacySupport { get; } = new() { IncludeLegacyPackages = true };
}
