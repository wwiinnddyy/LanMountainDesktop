namespace LanMountainDesktop.AirAppSdk;

public sealed record AirAppPublicIpcServiceRegistration(
    Type ContractType,
    string? ObjectId,
    string[] NotifyIds);
