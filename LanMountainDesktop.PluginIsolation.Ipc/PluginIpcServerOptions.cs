using System.Text.Json.Serialization;
using LanMountainDesktop.PluginIsolation.Contracts;

namespace LanMountainDesktop.PluginIsolation.Ipc;

public sealed record PluginIpcServerOptions
{
    public required string PipeName { get; init; }

    public string ProtocolVersion { get; init; } = PluginIsolationProtocolVersion.Current;

    public TimeSpan HeartbeatInterval { get; init; } = PluginIpcConstants.DefaultHeartbeatInterval;

    public TimeSpan HeartbeatTimeout { get; init; } = PluginIpcConstants.DefaultHeartbeatTimeout;

    public JsonSerializerContext SerializerContext { get; init; } = PluginIsolationJsonContext.Default;
}
