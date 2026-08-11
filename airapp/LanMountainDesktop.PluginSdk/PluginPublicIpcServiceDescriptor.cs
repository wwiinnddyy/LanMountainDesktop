namespace LanMountainDesktop.PluginSdk;

public sealed record PluginPublicIpcServiceDescriptor(
    Type ContractType,
    object Implementation,
    string? ObjectId,
    string[] NotifyIds);
