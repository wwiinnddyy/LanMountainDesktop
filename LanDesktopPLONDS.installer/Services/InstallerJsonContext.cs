using System.Text.Json.Serialization;

namespace LanDesktopPLONDS.Installer.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(InstallerPlondsManifest))]
internal sealed partial class InstallerJsonContext : JsonSerializerContext;
