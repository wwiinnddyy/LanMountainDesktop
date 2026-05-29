using System.Text.Json.Serialization;

namespace LanMountainDesktop.Services.Update;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ApplyPlondsFileMap))]
[JsonSerializable(typeof(ApplyPlondsUpdateMetadata))]
[JsonSerializable(typeof(ApplySnapshotMetadata))]
[JsonSerializable(typeof(ApplyInstallCheckpoint))]
internal sealed partial class UpdateApplyJsonContext : JsonSerializerContext;
