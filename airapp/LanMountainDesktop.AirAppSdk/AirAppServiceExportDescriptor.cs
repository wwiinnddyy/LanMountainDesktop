namespace LanMountainDesktop.AirAppSdk;

public sealed record AirAppServiceExportDescriptor(
    string ProviderAirAppId,
    Type ContractType,
    object ServiceInstance);
