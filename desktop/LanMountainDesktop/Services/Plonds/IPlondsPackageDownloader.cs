namespace LanMountainDesktop.Services.Plonds;

internal interface IPlondsPackageDownloader
{
    Task<PlondsPreparedPackage> PrepareDeltaAsync(
        PlondsClientManifest manifest,
        PlondsSourceDescriptor source,
        CancellationToken cancellationToken);

    Task<PlondsPreparedPackage> PrepareFullAsync(
        PlondsClientManifest manifest,
        PlondsSourceDescriptor source,
        CancellationToken cancellationToken);
}
