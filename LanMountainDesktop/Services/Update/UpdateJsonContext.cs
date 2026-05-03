using System.Text.Json;
using System.Text.Json.Serialization;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(InstallProgressReport))]
[JsonSerializable(typeof(InstallCompleteReport))]
[JsonSerializable(typeof(InstallRequest))]
[JsonSerializable(typeof(LaunchResult))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;
