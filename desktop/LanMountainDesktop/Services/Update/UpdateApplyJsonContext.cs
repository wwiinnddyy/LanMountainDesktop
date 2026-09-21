using System.Text.Json.Serialization;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ApplyPlondsFileMap))]
[JsonSerializable(typeof(ApplyPlondsUpdateMetadata))]
[JsonSerializable(typeof(SnapshotMetadata))]
[JsonSerializable(typeof(ApplyInstallCheckpoint))]
internal sealed partial class UpdateApplyJsonContext : JsonSerializerContext;
