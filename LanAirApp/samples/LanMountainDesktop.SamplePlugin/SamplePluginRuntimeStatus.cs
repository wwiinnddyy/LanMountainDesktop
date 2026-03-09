using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.SamplePlugin;

internal enum SamplePluginHealthState
{
    Healthy,
    Pending,
    Faulted
}

internal sealed record SamplePluginStatusEntry(
    string Key,
    string Title,
    SamplePluginHealthState State,
    string Summary,
    string Detail,
    DateTimeOffset UpdatedAt);

internal sealed record SamplePluginCapabilityItem(
    string Title,
    string Detail);

internal sealed record SamplePluginRuntimeSnapshot(
    PluginManifest Manifest,
    string PluginDirectory,
    string DataDirectory,
    string HostApplicationName,
    string HostVersion,
    string SdkApiVersion,
    IReadOnlyList<SamplePluginStatusEntry> StatusEntries,
    bool HasPlacedComponent,
    int PlacedCount,
    int PreviewCount,
    IReadOnlyList<string> PlacementIds,
    string? LastComponentId,
    double LastCellSize,
    DateTimeOffset? ServiceClockTime);

internal sealed record SamplePluginClockTickMessage(DateTimeOffset CurrentTime);

internal sealed record SamplePluginStateChangedMessage(string Reason);

internal sealed record SamplePluginComponentInstance(
    string ComponentId,
    string? PlacementId,
    double CellSize)
{
    public bool IsPlaced => !string.IsNullOrWhiteSpace(PlacementId);
}

internal sealed class SamplePluginRuntimeStateService
{
    private readonly object _gate = new();
    private readonly IPluginMessageBus _messageBus;
    private readonly Dictionary<string, SamplePluginComponentInstance> _componentInstances =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly PluginManifest _manifest;
    private readonly string _pluginDirectory;
    private readonly string _dataDirectory;
    private readonly string _hostApplicationName;
    private readonly string _hostVersion;
    private readonly string _sdkApiVersion;

    private SamplePluginStatusEntry _frontend;
    private SamplePluginStatusEntry _component;
    private SamplePluginStatusEntry _backend;
    private SamplePluginStatusEntry _service;
    private string? _lastComponentId;
    private double _lastCellSize;
    private DateTimeOffset? _serviceClockTime;

    public SamplePluginRuntimeStateService(
        PluginManifest manifest,
        string pluginDirectory,
        string dataDirectory,
        string hostApplicationName,
        string hostVersion,
        string sdkApiVersion,
        IPluginMessageBus messageBus)
    {
        _manifest = manifest;
        _pluginDirectory = pluginDirectory;
        _dataDirectory = dataDirectory;
        _hostApplicationName = hostApplicationName;
        _hostVersion = hostVersion;
        _sdkApiVersion = sdkApiVersion;
        _messageBus = messageBus;

        _frontend = CreateEntry(
            "frontend",
            "Frontend",
            SamplePluginHealthState.Pending,
            "Pending",
            "Waiting for a plugin UI surface to connect.");

        _component = CreateEntry(
            "component",
            "Component",
            SamplePluginHealthState.Pending,
            "Pending",
            "No component instance has been created yet.");

        _backend = CreateEntry(
            "backend",
            "Backend",
            SamplePluginHealthState.Pending,
            "Pending",
            "Plugin initialization is in progress.");

        _service = CreateEntry(
            "service",
            "Clock Service",
            SamplePluginHealthState.Pending,
            "Pending",
            "Clock service is not attached yet.");
    }

    public void AttachClockService(SamplePluginClockService clockService)
    {
        ArgumentNullException.ThrowIfNull(clockService);

        lock (_gate)
        {
            _serviceClockTime = clockService.CurrentTime;
            _service = CreateEntry(
                "service",
                "Clock Service",
                SamplePluginHealthState.Pending,
                "Attached",
                "Clock service was attached and is waiting for the first tick.");
        }

        PublishStateChanged("Clock service attached");
    }

    public void MarkFrontendReady(string detail)
    {
        lock (_gate)
        {
            _frontend = CreateEntry(
                "frontend",
                "Frontend",
                SamplePluginHealthState.Healthy,
                "Healthy",
                detail);
        }

        PublishStateChanged("Frontend updated");
    }

    public void MarkBackendReady(string detail)
    {
        lock (_gate)
        {
            _backend = CreateEntry(
                "backend",
                "Backend",
                SamplePluginHealthState.Healthy,
                "Healthy",
                detail);
        }

        PublishStateChanged("Backend updated");
    }

    public void MarkBackendFaulted(string detail)
    {
        lock (_gate)
        {
            _backend = CreateEntry(
                "backend",
                "Backend",
                SamplePluginHealthState.Faulted,
                "Faulted",
                detail);
        }

        PublishStateChanged("Backend faulted");
    }

    public void MarkClockServiceTick(DateTimeOffset currentTime)
    {
        lock (_gate)
        {
            _serviceClockTime = currentTime;
            _service = CreateEntry(
                "service",
                "Clock Service",
                SamplePluginHealthState.Healthy,
                "Healthy",
                $"Clock service is running. Current service time: {currentTime.LocalDateTime:HH:mm:ss}");
        }

        PublishStateChanged("Clock service tick");
    }

    public void MarkClockServiceFaulted(string detail)
    {
        lock (_gate)
        {
            _service = CreateEntry(
                "service",
                "Clock Service",
                SamplePluginHealthState.Faulted,
                "Faulted",
                detail);
        }

        PublishStateChanged("Clock service faulted");
    }

    public string RegisterComponentInstance(string componentId, string? placementId, double cellSize)
    {
        var instanceId = Guid.NewGuid().ToString("N");

        lock (_gate)
        {
            _componentInstances[instanceId] = new SamplePluginComponentInstance(componentId, placementId, cellSize);
            _lastComponentId = componentId;
            _lastCellSize = cellSize;
            UpdateComponentStatusNoLock();
        }

        PublishStateChanged("Component attached");
        return instanceId;
    }

    public void UnregisterComponentInstance(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var removed = false;
        lock (_gate)
        {
            removed = _componentInstances.Remove(instanceId);
            if (removed)
            {
                UpdateComponentStatusNoLock();
            }
        }

        if (removed)
        {
            PublishStateChanged("Component detached");
        }
    }

    public SamplePluginRuntimeSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var placementIds = _componentInstances.Values
                .Where(instance => instance.IsPlaced)
                .Select(instance => instance.PlacementId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var previewCount = _componentInstances.Values.Count(instance => !instance.IsPlaced);

            return new SamplePluginRuntimeSnapshot(
                _manifest,
                _pluginDirectory,
                _dataDirectory,
                _hostApplicationName,
                _hostVersion,
                _sdkApiVersion,
                [_frontend, _component, _backend, _service],
                placementIds.Length > 0,
                placementIds.Length,
                previewCount,
                placementIds,
                _lastComponentId,
                _lastCellSize,
                _serviceClockTime);
        }
    }

    public IReadOnlyList<SamplePluginCapabilityItem> GetCapabilities(
        IPluginContext context,
        bool hasStateService,
        bool hasClockService,
        bool hasMessageBus)
    {
        ArgumentNullException.ThrowIfNull(context);

        var propertyNames = context.Properties.Count == 0
            ? "(none)"
            : string.Join(", ", context.Properties.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));

        return
        [
            new SamplePluginCapabilityItem(
                "IPluginContext.Manifest",
                $"Readable. Current plugin id: {context.Manifest.Id}; version: {context.Manifest.Version ?? "dev"}."),
            new SamplePluginCapabilityItem(
                "IPluginContext.PluginDirectory / DataDirectory",
                $"Readable. Plugin directory: {context.PluginDirectory}; data directory: {context.DataDirectory}."),
            new SamplePluginCapabilityItem(
                "IPluginContext.Properties",
                $"Readable. Host properties currently exposed: {propertyNames}."),
            new SamplePluginCapabilityItem(
                "IPluginContext.GetService<T>()",
                $"Callable. State service resolved: {hasStateService}; clock service resolved: {hasClockService}; message bus resolved: {hasMessageBus}."),
            new SamplePluginCapabilityItem(
                "IPluginContext.RegisterService<TService>()",
                "Callable during plugin initialization. This plugin registers SamplePluginRuntimeStateService and SamplePluginClockService into the plugin service container."),
            new SamplePluginCapabilityItem(
                "Plugin communication bus",
                "This plugin uses IPluginMessageBus to push clock ticks and state change notifications into plugin UI surfaces."),
            new SamplePluginCapabilityItem(
                "PluginDesktopComponentContext",
                "Widgets can read ComponentId, PlacementId, CellSize, and call GetService<T>() against the same plugin service container.")
        ];
    }

    private void UpdateComponentStatusNoLock()
    {
        var placementIds = _componentInstances.Values
            .Where(instance => instance.IsPlaced)
            .Select(instance => instance.PlacementId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var previewCount = _componentInstances.Values.Count(instance => !instance.IsPlaced);

        if (placementIds.Length > 0)
        {
            _component = CreateEntry(
                "component",
                "Component",
                SamplePluginHealthState.Healthy,
                "Placed",
                $"Placed count: {placementIds.Length}; preview count: {previewCount}; placements: {string.Join(", ", placementIds)}");
            return;
        }

        if (previewCount > 0)
        {
            _component = CreateEntry(
                "component",
                "Component",
                SamplePluginHealthState.Healthy,
                "Preview",
                $"Preview instances: {previewCount}; no placed desktop instance is active yet.");
            return;
        }

        _component = CreateEntry(
            "component",
            "Component",
            SamplePluginHealthState.Pending,
            "Pending",
            "No component instance is active.");
    }

    private void PublishStateChanged(string reason)
    {
        _messageBus.Publish(new SamplePluginStateChangedMessage(reason));
    }

    private static SamplePluginStatusEntry CreateEntry(
        string key,
        string title,
        SamplePluginHealthState state,
        string summary,
        string detail)
    {
        return new SamplePluginStatusEntry(
            key,
            title,
            state,
            summary,
            detail,
            DateTimeOffset.Now);
    }
}

internal sealed class SamplePluginClockService : IDisposable
{
    private readonly object _gate = new();
    private readonly string _clockStateFilePath;
    private readonly SamplePluginRuntimeStateService _stateService;
    private readonly IPluginMessageBus _messageBus;
    private readonly Timer _timer;
    private DateTimeOffset _currentTime = DateTimeOffset.Now;
    private int _disposed;

    public SamplePluginClockService(
        string dataDirectory,
        SamplePluginRuntimeStateService stateService,
        IPluginMessageBus messageBus)
    {
        _clockStateFilePath = Path.Combine(dataDirectory, "clock-service.txt");
        _stateService = stateService;
        _messageBus = messageBus;
        _timer = new Timer(OnTimerTick);
    }

    public DateTimeOffset CurrentTime
    {
        get
        {
            lock (_gate)
            {
                return _currentTime;
            }
        }
    }

    public void Start()
    {
        PublishTick();
        _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timer.Dispose();
    }

    private void OnTimerTick(object? state)
    {
        PublishTick();
    }

    private void PublishTick()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        lock (_gate)
        {
            _currentTime = now;
        }

        try
        {
            File.WriteAllText(
                _clockStateFilePath,
                now.ToString("O", CultureInfo.InvariantCulture));
            _stateService.MarkClockServiceTick(now);
            _messageBus.Publish(new SamplePluginClockTickMessage(now));
        }
        catch (Exception ex)
        {
            _stateService.MarkClockServiceFaulted($"Clock state write failed: {ex.Message}");
        }
    }
}
