namespace LanMountainDesktop.Shared.IPC;

public sealed record PublicIpcServiceDescriptor(
    string ContractTypeName,
    string ContractAssemblyName,
    string? ContractAssemblyQualifiedName,
    string? ObjectId,
    string? PluginId,
    bool IsBuiltIn,
    string[] NotifyIds);
