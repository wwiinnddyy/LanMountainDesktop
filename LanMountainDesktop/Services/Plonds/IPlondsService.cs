namespace LanMountainDesktop.Services.Plonds;

internal interface IPlondsService
{
    Task<PlondsLatestResult> FindLatestAsync(Version currentVersion, CancellationToken cancellationToken);

    Task<PlondsPrepareResult> FindAndPrepareLatestAsync(CancellationToken cancellationToken);

    Task<PlondsPrepareResult> FindAndPrepareLatestAsync(Version currentVersion, CancellationToken cancellationToken);

    Task<PlondsPrepareResult> FindAndPrepareLatestAsync(
        Version currentVersion,
        bool forceFullPackage,
        CancellationToken cancellationToken);
}
