using System.Text.Json.Serialization;

namespace LanMountainDesktop.PluginSdk;

public sealed record PluginSharedContractReference(
    string Id,
    string Version,
    string AssemblyName)
{
    [JsonIgnore]
    public string NormalizedId => Id.Trim();

    [JsonIgnore]
    public string NormalizedVersion => Version.Trim();

    [JsonIgnore]
    public string NormalizedAssemblyName => AssemblyName.Trim();

    internal PluginSharedContractReference NormalizeAndValidate(string manifestPath)
    {
        var normalized = this with
        {
            Id = RequireValue(Id, nameof(Id), manifestPath),
            Version = RequireValue(Version, nameof(Version), manifestPath),
            AssemblyName = RequireValue(AssemblyName, nameof(AssemblyName), manifestPath)
        };

        if (!System.Version.TryParse(normalized.Version, out _))
        {
            throw new InvalidOperationException(
                $"Plugin manifest '{manifestPath}' declares invalid shared contract version '{normalized.Version}' for '{normalized.Id}'.");
        }

        return normalized;
    }

    private static string RequireValue(string? value, string propertyName, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Plugin manifest '{manifestPath}' is missing required shared contract property '{propertyName}'.");
        }

        return value.Trim();
    }
}
