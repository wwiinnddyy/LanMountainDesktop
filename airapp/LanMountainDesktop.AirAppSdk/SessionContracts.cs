namespace LanMountainDesktop.AirAppIsolation.Contracts;

public sealed record AirAppSessionHandshakeRequest(
    string AirAppId,
    string SessionId,
    string RuntimeMode,
    string ProtocolVersion,
    IReadOnlyList<AirAppCapabilityDeclaration>? RequestedCapabilities = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record AirAppSessionHandshakeResponse(
    bool Accepted,
    string ProtocolVersion,
    IReadOnlyList<AirAppCapabilityDeclaration>? GrantedCapabilities = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record AirAppReadyNotification(
    string AirAppId,
    string SessionId,
    IReadOnlyList<AirAppUiSurfaceDescriptor>? UiSurfaces = null);
