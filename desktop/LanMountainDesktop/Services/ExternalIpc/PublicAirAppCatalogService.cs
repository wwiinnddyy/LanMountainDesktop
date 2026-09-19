using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Services.ExternalIpc;

internal sealed class PublicAirAppCatalogService : IPublicAirAppCatalogService
{
    private readonly PublicIpcHostService _publicIpcHostService;

    public PublicAirAppCatalogService(PublicIpcHostService publicIpcHostService)
    {
        _publicIpcHostService = publicIpcHostService;
    }

    public PublicIpcCatalogSnapshot GetCatalog()
    {
        return _publicIpcHostService.GetCatalogSnapshot();
    }
}
