using System.Text.Json.Serialization;
using LanMountainDesktop.AirAppIsolation.Contracts;

namespace LanMountainDesktop.AirAppIsolation.Ipc;

public sealed record AirAppIpcClientOptions
{
    public required string PipeName { get; init; }

    public string ProtocolVersion { get; init; } = AirAppIsolationProtocolVersion.Current;

    public TimeSpan ConnectTimeout { get; init; } = AirAppIpcConstants.DefaultConnectTimeout;

    public TimeSpan RequestTimeout { get; init; } = AirAppIpcConstants.DefaultRequestTimeout;

    public JsonSerializerContext SerializerContext { get; init; } = AirAppIsolationJsonContext.Default;
}
