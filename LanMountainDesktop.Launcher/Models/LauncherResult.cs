using System.Text.Json.Serialization;

namespace LanMountainDesktop.Launcher.Models;

internal sealed class LauncherResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } = "ok";

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("currentVersion")]
    public string? CurrentVersion { get; init; }

    [JsonPropertyName("targetVersion")]
    public string? TargetVersion { get; init; }

    [JsonPropertyName("rolledBackTo")]
    public string? RolledBackTo { get; init; }

    [JsonPropertyName("details")]
    public Dictionary<string, string> Details { get; init; } = [];

    [JsonPropertyName("installedPackagePath")]
    public string? InstalledPackagePath { get; init; }

    [JsonPropertyName("manifestId")]
    public string? ManifestId { get; init; }

    [JsonPropertyName("manifestName")]
    public string? ManifestName { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }
}
