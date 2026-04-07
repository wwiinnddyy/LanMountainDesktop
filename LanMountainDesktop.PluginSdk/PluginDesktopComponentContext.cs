namespace LanMountainDesktop.PluginSdk;

public sealed class PluginDesktopComponentContext
{
    public PluginDesktopComponentContext(
        PluginManifest manifest,
        string pluginDirectory,
        string dataDirectory,
        IServiceProvider services,
        IReadOnlyDictionary<string, object?> properties,
        string componentId,
        string? placementId,
        double cellSize,
        IPluginAppearanceContext appearance,
        IPluginSettingsService? pluginSettings = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(appearance);

        Manifest = manifest;
        PluginDirectory = pluginDirectory;
        DataDirectory = dataDirectory;
        Services = services;
        Properties = properties;
        ComponentId = componentId.Trim();
        PlacementId = string.IsNullOrWhiteSpace(placementId) ? null : placementId.Trim();
        CellSize = Math.Max(1, cellSize);
        Appearance = appearance;
        PluginSettings = pluginSettings;
    }

    public PluginManifest Manifest { get; }

    public string PluginDirectory { get; }

    public string DataDirectory { get; }

    public IServiceProvider Services { get; }

    public IReadOnlyDictionary<string, object?> Properties { get; }

    public string ComponentId { get; }

    public string? PlacementId { get; }

    public double CellSize { get; }

    public IPluginAppearanceContext Appearance { get; }

    public PluginCornerRadiusTokens CornerRadiusTokens => Appearance.Snapshot.CornerRadiusTokens;

    public IPluginSettingsService? PluginSettings { get; }

    public double ResolveScaledCornerRadius(double baseRadius, double? minimum = null, double? maximum = null)
    {
        return Appearance.ResolveScaledCornerRadius(baseRadius, minimum, maximum);
    }

    public double ResolveCornerRadius(PluginCornerRadiusPreset preset, double? minimum = null, double? maximum = null)
    {
        return Appearance.ResolveCornerRadius(preset, minimum, maximum);
    }

    public T? GetService<T>()
    {
        return (T?)Services.GetService(typeof(T));
    }

    public bool TryGetProperty<T>(string key, out T? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (Properties.TryGetValue(key, out var rawValue) && rawValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }
}
