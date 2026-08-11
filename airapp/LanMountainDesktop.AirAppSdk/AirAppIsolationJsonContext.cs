using System.Text.Json.Serialization;

namespace LanMountainDesktop.AirAppIsolation.Contracts;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AirAppCapabilityDeclaration))]
[JsonSerializable(typeof(List<AirAppCapabilityDeclaration>))]
[JsonSerializable(typeof(AirAppSessionHandshakeRequest))]
[JsonSerializable(typeof(AirAppSessionHandshakeResponse))]
[JsonSerializable(typeof(AirAppReadyNotification))]
[JsonSerializable(typeof(AirAppInitializeRequest))]
[JsonSerializable(typeof(AirAppInitializeResponse))]
[JsonSerializable(typeof(AirAppStopRequest))]
[JsonSerializable(typeof(AirAppRestartRequest))]
[JsonSerializable(typeof(AirAppLifecycleStateChanged))]
[JsonSerializable(typeof(AirAppSettingsSnapshotRequest))]
[JsonSerializable(typeof(AirAppSettingsSnapshotResponse))]
[JsonSerializable(typeof(AirAppSettingsWriteRequest))]
[JsonSerializable(typeof(AirAppSettingsWriteResponse))]
[JsonSerializable(typeof(AirAppSettingsChangedNotification))]
[JsonSerializable(typeof(AirAppAppearanceSnapshotRequest))]
[JsonSerializable(typeof(AirAppMaterialSurfaceSnapshot))]
[JsonSerializable(typeof(AirAppAppearanceSnapshot))]
[JsonSerializable(typeof(AirAppAppearanceChangedNotification))]
[JsonSerializable(typeof(AirAppUiSurfaceDescriptor))]
[JsonSerializable(typeof(List<AirAppUiSurfaceDescriptor>))]
[JsonSerializable(typeof(AirAppUiAttachRequest))]
[JsonSerializable(typeof(AirAppUiAttachResponse))]
[JsonSerializable(typeof(AirAppUiDetachNotification))]
[JsonSerializable(typeof(AirAppUiCommandRequest))]
[JsonSerializable(typeof(AirAppUiCommandResponse))]
[JsonSerializable(typeof(AirAppUiStateChangedNotification))]
[JsonSerializable(typeof(AirAppHeartbeatPing))]
[JsonSerializable(typeof(AirAppHeartbeatPong))]
[JsonSerializable(typeof(AirAppLogEntry))]
[JsonSerializable(typeof(AirAppFaultReport))]
public partial class AirAppIsolationJsonContext : JsonSerializerContext;
