using System.Text.Json.Serialization;
using LanMountainDesktop.PluginIsolation.Contracts;

namespace LanMountainDesktop.PluginIsolation.Ipc;

public sealed record PluginIpcClientOptions
{
    public required string PipeName { get; init; }

    public string ProtocolVersion { get; init; } = PluginIsolationProtocolVersion.Current;

    public TimeSpan ConnectTimeout { get; init; } = PluginIpcConstants.DefaultConnectTimeout;

    public TimeSpan RequestTimeout { get; init; } = PluginIpcConstants.DefaultRequestTimeout;

    public JsonSerializerContext SerializerContext { get; init; } = PluginIsolationJsonContext.Default;
}
