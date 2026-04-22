namespace LanMountainDesktop.PluginSdk;

public sealed record PluginPublicIpcServiceRegistration(
    Type ContractType,
    string? ObjectId,
    string[] NotifyIds);
