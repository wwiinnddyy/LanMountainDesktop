namespace LanMountainDesktop.AirAppSdk;

public sealed class AirAppComponentEditorContext
{
    public AirAppComponentEditorContext(
        AirAppManifest manifest,
        string airAppDirectory,
        string dataDirectory,
        IServiceProvider services,
        IReadOnlyDictionary<string, object?> properties,
        string componentId,
        string? placementId,
        IAirAppSettingsService? airAppSettings,
        IComponentEditorHostContext hostContext)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(airAppDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(hostContext);

        Manifest = manifest;
        AirAppDirectory = airAppDirectory;
        DataDirectory = dataDirectory;
        Services = services;
        Properties = properties;
        ComponentId = componentId.Trim();
        PlacementId = string.IsNullOrWhiteSpace(placementId) ? null : placementId.Trim();
        AirAppSettings = airAppSettings;
        HostContext = hostContext;
    }

    public AirAppManifest Manifest { get; }

    public string AirAppDirectory { get; }

    public string DataDirectory { get; }

    public IServiceProvider Services { get; }

    public IReadOnlyDictionary<string, object?> Properties { get; }

    public string ComponentId { get; }

    public string? PlacementId { get; }

    public IAirAppSettingsService? AirAppSettings { get; }

    public IComponentEditorHostContext HostContext { get; }

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
