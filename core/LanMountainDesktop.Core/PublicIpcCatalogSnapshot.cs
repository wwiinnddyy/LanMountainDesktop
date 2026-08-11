namespace LanMountainDesktop.Shared.IPC;

public sealed record PublicIpcCatalogSnapshot(
    PublicIpcServiceDescriptor[] Services,
    PublicPluginDescriptor[] Plugins,
    DateTimeOffset Timestamp);
