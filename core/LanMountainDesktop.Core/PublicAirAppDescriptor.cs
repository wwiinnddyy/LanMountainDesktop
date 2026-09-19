namespace LanMountainDesktop.Shared.IPC;

public sealed record PublicAirAppDescriptor(
    string PluginId,
    string DisplayName,
    string? Version,
    bool IsLoaded,
    bool IsEnabled);
