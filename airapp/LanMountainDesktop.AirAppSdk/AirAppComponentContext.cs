namespace LanMountainDesktop.AirAppSdk;

public sealed class AirAppComponentContext
{
    public AirAppComponentContext(
        AirAppManifest manifest,
        string pluginDirectory,
        string dataDirectory,
        IServiceProvider services,
        IReadOnlyDictionary<string, object?> properties,
        string componentId,
        string? placementId,
        double cellSize,
        IAirAppAppearanceContext appearance,
        IAirAppSettingsService? pluginSettings = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(appearance);

        Manifest = manifest;
        AirAppDirectory = pluginDirectory;
        DataDirectory = dataDirectory;
        Services = services;
        Properties = properties;
        ComponentId = componentId.Trim();
        PlacementId = string.IsNullOrWhiteSpace(placementId) ? null : placementId.Trim();
        CellSize = Math.Max(1, cellSize);
        Appearance = appearance;
        AirAppSettings = pluginSettings;
    }

    public AirAppManifest Manifest { get; }

    public string AirAppDirectory { get; }

    public string DataDirectory { get; }

    public IServiceProvider Services { get; }

    public IReadOnlyDictionary<string, object?> Properties { get; }

    public string ComponentId { get; }

    public string? PlacementId { get; }

    public double CellSize { get; }

    public IAirAppAppearanceContext Appearance { get; }

    public AirAppCornerRadiusTokens CornerRadiusTokens => Appearance.Snapshot.CornerRadiusTokens;

    public IAirAppSettingsService? AirAppSettings { get; }

    /// <summary>
    /// 由宿主注入的窗口打开处理器。组件通过 <see cref="OpenWindowAsync"/> 打开本 AirApp 的窗口轻应用。
    /// </summary>
    public Func<string, Task>? OpenWindowHandler { get; set; }

    public double ResolveScaledCornerRadius(double baseRadius, double? minimum = null, double? maximum = null)
    {
        return Appearance.ResolveScaledCornerRadius(baseRadius, minimum, maximum);
    }

    public double ResolveCornerRadius(AirAppCornerRadiusPreset preset, double? minimum = null, double? maximum = null)
    {
        return Appearance.ResolveCornerRadius(preset, minimum, maximum);
    }

    /// <summary>
    /// 打开本 AirApp 声明的一个窗口轻应用。
    /// </summary>
    /// <param name="windowId">窗口标识（与 <c>AddAirAppWindow</c> 注册的 id 一致）。</param>
    public Task OpenWindowAsync(string windowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);

        var handler = OpenWindowHandler;
        if (handler is null)
        {
            throw new NotSupportedException(
                $"AirApp '{Manifest.Id}' component '{ComponentId}' requested to open window '{windowId}', but the host does not support window opening for this component.");
        }

        return handler(windowId);
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
