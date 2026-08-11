namespace LanMountainDesktop.AirAppSdk;

public interface IAirAppExportRegistry
{
    IReadOnlyList<AirAppServiceExportDescriptor> GetExports();

    IReadOnlyList<AirAppServiceExportDescriptor> GetExports(Type contractType);

    AirAppServiceExportDescriptor? GetExport(Type contractType, string providerAirAppId);

    TContract? GetExport<TContract>(string providerAirAppId)
        where TContract : class;
}
