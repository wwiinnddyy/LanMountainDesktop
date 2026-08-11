namespace LanMountainDesktop.AirAppSdk;

public sealed record AirAppPublicIpcServiceDescriptor(
    Type ContractType,
    object Implementation,
    string? ObjectId,
    string[] NotifyIds);
