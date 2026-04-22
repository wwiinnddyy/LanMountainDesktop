using System.Text.Json.Serialization;

namespace LanMountainDesktop.PluginIsolation.Contracts;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PluginCapabilityDeclaration))]
[JsonSerializable(typeof(List<PluginCapabilityDeclaration>))]
[JsonSerializable(typeof(PluginSessionHandshakeRequest))]
[JsonSerializable(typeof(PluginSessionHandshakeResponse))]
[JsonSerializable(typeof(PluginReadyNotification))]
[JsonSerializable(typeof(PluginInitializeRequest))]
[JsonSerializable(typeof(PluginInitializeResponse))]
[JsonSerializable(typeof(PluginStopRequest))]
[JsonSerializable(typeof(PluginRestartRequest))]
[JsonSerializable(typeof(PluginLifecycleStateChanged))]
[JsonSerializable(typeof(PluginSettingsSnapshotRequest))]
[JsonSerializable(typeof(PluginSettingsSnapshotResponse))]
[JsonSerializable(typeof(PluginSettingsWriteRequest))]
[JsonSerializable(typeof(PluginSettingsWriteResponse))]
[JsonSerializable(typeof(PluginSettingsChangedNotification))]
[JsonSerializable(typeof(PluginAppearanceSnapshotRequest))]
[JsonSerializable(typeof(PluginAppearanceSnapshot))]
[JsonSerializable(typeof(PluginAppearanceChangedNotification))]
[JsonSerializable(typeof(PluginUiSurfaceDescriptor))]
[JsonSerializable(typeof(List<PluginUiSurfaceDescriptor>))]
[JsonSerializable(typeof(PluginUiAttachRequest))]
[JsonSerializable(typeof(PluginUiAttachResponse))]
[JsonSerializable(typeof(PluginUiDetachNotification))]
[JsonSerializable(typeof(PluginUiCommandRequest))]
[JsonSerializable(typeof(PluginUiCommandResponse))]
[JsonSerializable(typeof(PluginUiStateChangedNotification))]
[JsonSerializable(typeof(PluginHeartbeatPing))]
[JsonSerializable(typeof(PluginHeartbeatPong))]
[JsonSerializable(typeof(PluginLogEntry))]
[JsonSerializable(typeof(PluginFaultReport))]
public partial class PluginIsolationJsonContext : JsonSerializerContext;
