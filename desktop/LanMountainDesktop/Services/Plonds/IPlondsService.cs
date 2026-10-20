namespace LanMountainDesktop.Services.Plonds;

internal interface IPlondsService
{
    Task<PlondsPrepareResult> PrepareAsync(
        PlondsLatestResult latest, bool forceFullPackage, int downloadThreads,
        IProgress<DownloadProgressInfo>? progress, CancellationToken cancellationToken) =>
        FindAndPrepareLatestAsync(latest.CurrentVersion, forceFullPackage, cancellationToken);

    Task<PlondsLatestResult> FindLatestAsync(Version currentVersion, CancellationToken cancellationToken);

    Task<PlondsPrepareResult> FindAndPrepareLatestAsync(CancellationToken cancellationToken);

    Task<PlondsPrepareResult> FindAndPrepareLatestAsync(Version currentVersion, CancellationToken cancellationToken);

    Task<PlondsPrepareResult> FindAndPrepareLatestAsync(
        Version currentVersion,
        bool forceFullPackage,
        CancellationToken cancellationToken);
}
