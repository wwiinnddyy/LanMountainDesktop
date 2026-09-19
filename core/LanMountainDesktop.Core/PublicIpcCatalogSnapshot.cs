namespace LanMountainDesktop.Shared.IPC;

public sealed record PublicIpcCatalogSnapshot(
    PublicIpcServiceDescriptor[] Services,
    // 成员名即 IPC JSON 字段名（GetResponseAsync<PublicIpcCatalogSnapshot> 按名反序列化），冻结。
    PublicAirAppDescriptor[] Plugins,
    DateTimeOffset Timestamp);
