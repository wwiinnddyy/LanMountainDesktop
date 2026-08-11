namespace LanMountainDesktop.Shared.IPC;

public sealed record PublicPluginDescriptor(
    string PluginId,
    string DisplayName,
    string? Version,
    bool IsLoaded,
    bool IsEnabled);
