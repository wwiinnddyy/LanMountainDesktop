namespace LanMountainDesktop.Shared.IPC.DependencyInjection;

public sealed record PublicIpcServiceRegistration(
    Type ContractType,
    Func<IServiceProvider, object> ImplementationFactory,
    string? ObjectId,
    string? PluginId,
    string[] NotifyIds);
