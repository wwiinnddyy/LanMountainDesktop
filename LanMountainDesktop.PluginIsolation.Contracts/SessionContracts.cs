namespace LanMountainDesktop.PluginIsolation.Contracts;

public sealed record PluginSessionHandshakeRequest(
    string PluginId,
    string SessionId,
    string RuntimeMode,
    string ProtocolVersion,
    IReadOnlyList<PluginCapabilityDeclaration>? RequestedCapabilities = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record PluginSessionHandshakeResponse(
    bool Accepted,
    string ProtocolVersion,
    IReadOnlyList<PluginCapabilityDeclaration>? GrantedCapabilities = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record PluginReadyNotification(
    string PluginId,
    string SessionId,
    IReadOnlyList<PluginUiSurfaceDescriptor>? UiSurfaces = null);
