using Plonds.Shared.Models;

namespace Plonds.Api.Services;

public interface IPlondsManifestStore
{
    Task<PlondsMetadataCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);

    Task<PlondsChannelPointer?> GetLatestAsync(string channel, string platform, CancellationToken cancellationToken = default);

    Task<PlondsDistributionInfo?> GetDistributionAsync(string distributionId, CancellationToken cancellationToken = default);
}

