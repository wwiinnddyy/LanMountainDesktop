namespace LanMountainDesktop.Services.Plonds;

internal interface IPlondsPackageDownloader
{
    void ConfigureTransfer(int downloadThreads, IProgress<DownloadProgressInfo>? progress) { }

    Task<PlondsPreparedPackage> PrepareDeltaAsync(
        PlondsClientManifest manifest,
        PlondsSourceDescriptor source,
        CancellationToken cancellationToken);

    Task<PlondsPreparedPackage> PrepareFullAsync(
        PlondsClientManifest manifest,
        PlondsSourceDescriptor source,
        CancellationToken cancellationToken);
}
