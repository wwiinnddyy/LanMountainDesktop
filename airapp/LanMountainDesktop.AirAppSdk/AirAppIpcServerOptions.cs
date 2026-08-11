using System.Text.Json.Serialization;
using LanMountainDesktop.AirAppIsolation.Contracts;

namespace LanMountainDesktop.AirAppIsolation.Ipc;

public sealed record AirAppIpcServerOptions
{
    public required string PipeName { get; init; }

    public string ProtocolVersion { get; init; } = AirAppIsolationProtocolVersion.Current;

    public TimeSpan HeartbeatInterval { get; init; } = AirAppIpcConstants.DefaultHeartbeatInterval;

    public TimeSpan HeartbeatTimeout { get; init; } = AirAppIpcConstants.DefaultHeartbeatTimeout;

    public JsonSerializerContext SerializerContext { get; init; } = AirAppIsolationJsonContext.Default;
}
