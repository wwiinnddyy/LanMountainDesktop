namespace LanMountainDesktop.PluginSdk;

public sealed record PluginServiceExportDescriptor(
    string ProviderPluginId,
    Type ContractType,
    object ServiceInstance);
