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
    private readonly PluginLocalizer _localizer;

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
        IPluginMessageBus messageBus,
        PluginLocalizer localizer)
    {
        _manifest = manifest;
        _pluginDirectory = pluginDirectory;
        _dataDirectory = dataDirectory;
        _hostApplicationName = hostApplicationName;
        _hostVersion = hostVersion;
        _sdkApiVersion = sdkApiVersion;
        _messageBus = messageBus;
        _localizer = localizer;

        _frontend = CreateEntry(
            "frontend",
            T("status.frontend.title", "前端状态"),
            SamplePluginHealthState.Pending,
            T("status.summary.pending", "等待中"),
            T("status.frontend.detail.pending", "等待插件界面接入。"));

        _component = CreateEntry(
            "component",
            T("status.component.title", "组件状态"),
            SamplePluginHealthState.Pending,
            T("status.summary.pending", "等待中"),
            T("status.component.detail.pending", "当前还没有创建组件实例。"));

        _backend = CreateEntry(
            "backend",
            T("status.backend.title", "后端状态"),
            SamplePluginHealthState.Pending,
            T("status.summary.pending", "等待中"),
            T("status.backend.detail.pending", "插件初始化进行中。"));

        _service = CreateEntry(
            "service",
            T("status.service.title", "时钟服务"),
            SamplePluginHealthState.Pending,
            T("status.summary.pending", "等待中"),
            T("status.service.detail.pending", "时钟服务尚未挂接。"));
    }

    public void AttachClockService(SamplePluginClockService clockService)
    {
        ArgumentNullException.ThrowIfNull(clockService);

        lock (_gate)
        {
            _serviceClockTime = clockService.CurrentTime;
            _service = CreateEntry(
                "service",
                T("status.service.title", "时钟服务"),
                SamplePluginHealthState.Pending,
                T("status.summary.attached", "已挂接"),
                T("status.service.detail.attached", "时钟服务已挂接，正在等待第一次心跳。"));
        }

        PublishStateChanged("Clock service attached");
    }

    public void MarkFrontendReady(string detail)
    {
        lock (_gate)
        {
            _frontend = CreateEntry(
                "frontend",
                T("status.frontend.title", "前端状态"),
                SamplePluginHealthState.Healthy,
                T("status.summary.healthy", "正常"),
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
                T("status.backend.title", "后端状态"),
                SamplePluginHealthState.Healthy,
                T("status.summary.healthy", "正常"),
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
                T("status.backend.title", "后端状态"),
                SamplePluginHealthState.Faulted,
                T("status.summary.faulted", "异常"),
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
                T("status.service.title", "时钟服务"),
                SamplePluginHealthState.Healthy,
                T("status.summary.healthy", "正常"),
                Tf(
                    "status.service.detail.running",
                    "时钟服务运行中，当前服务时间：{0}",
                    currentTime.LocalDateTime.ToString("HH:mm:ss")));
        }

        PublishStateChanged("Clock service tick");
    }

    public void MarkClockServiceFaulted(string detail)
    {
        lock (_gate)
        {
            _service = CreateEntry(
                "service",
                T("status.service.title", "时钟服务"),
                SamplePluginHealthState.Faulted,
                T("status.summary.faulted", "异常"),
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
            ? T("common.none", "（无）")
            : string.Join(", ", context.Properties.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));

        return
        [
            new SamplePluginCapabilityItem(
                T("capability.manifest.title", "IPluginContext.Manifest"),
                Tf(
                    "capability.manifest.detail",
                    "可读取。当前插件 id：{0}；版本：{1}。",
                    context.Manifest.Id,
                    context.Manifest.Version ?? T("common.dev", "开发版"))),
            new SamplePluginCapabilityItem(
                T("capability.directories.title", "IPluginContext.PluginDirectory / DataDirectory"),
                Tf(
                    "capability.directories.detail",
                    "可读取。插件目录：{0}；数据目录：{1}。",
                    context.PluginDirectory,
                    context.DataDirectory)),
            new SamplePluginCapabilityItem(
                T("capability.properties.title", "IPluginContext.Properties"),
                Tf(
                    "capability.properties.detail",
                    "可读取。宿主当前暴露的属性：{0}。",
                    propertyNames)),
            new SamplePluginCapabilityItem(
                T("capability.get_service.title", "IPluginContext.GetService<T>()"),
                Tf(
                    "capability.get_service.detail",
                    "可调用。状态服务已解析：{0}；时钟服务已解析：{1}；消息总线已解析：{2}。",
                    FormatBoolean(hasStateService),
                    FormatBoolean(hasClockService),
                    FormatBoolean(hasMessageBus))),
            new SamplePluginCapabilityItem(
                T("capability.register_service.title", "IPluginContext.RegisterService<TService>()"),
                T(
                    "capability.register_service.detail",
                    "可在插件初始化阶段调用。这个示例插件会把 SamplePluginRuntimeStateService 和 SamplePluginClockService 注册进插件服务容器。")),
            new SamplePluginCapabilityItem(
                T("capability.message_bus.title", "插件通信总线"),
                T(
                    "capability.message_bus.detail",
                    "这个示例插件通过 IPluginMessageBus 向插件 UI 推送时钟心跳和状态变化通知。")),
            new SamplePluginCapabilityItem(
                T("capability.widget_context.title", "PluginDesktopComponentContext"),
                T(
                    "capability.widget_context.detail",
                    "组件可以读取 ComponentId、PlacementId、CellSize，并能在同一个插件服务容器上调用 GetService<T>()。"))
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
                T("status.component.title", "组件状态"),
                SamplePluginHealthState.Healthy,
                T("status.summary.placed", "已放置"),
                Tf(
                    "status.component.detail.placed",
                    "已放置数量：{0}；预览数量：{1}；放置位置：{2}",
                    placementIds.Length,
                    previewCount,
                    string.Join(", ", placementIds)));
            return;
        }

        if (previewCount > 0)
        {
            _component = CreateEntry(
                "component",
                T("status.component.title", "组件状态"),
                SamplePluginHealthState.Healthy,
                T("status.summary.preview", "预览中"),
                Tf(
                    "status.component.detail.preview",
                    "当前预览实例数量：{0}；尚未有已放置的桌面实例。",
                    previewCount));
            return;
        }

        _component = CreateEntry(
            "component",
            T("status.component.title", "组件状态"),
            SamplePluginHealthState.Pending,
            T("status.summary.pending", "等待中"),
            T("status.component.detail.none", "当前没有活动中的组件实例。"));
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

    private string T(string key, string fallback)
    {
        return _localizer.GetString(key, fallback);
    }

    private string Tf(string key, string fallback, params object[] args)
    {
        return _localizer.Format(key, fallback, args);
    }

    private string FormatBoolean(bool value)
    {
        return value
            ? T("common.true", "是")
            : T("common.false", "否");
    }
}

internal sealed class SamplePluginClockService : IDisposable
{
    private readonly object _gate = new();
    private readonly string _clockStateFilePath;
    private readonly SamplePluginRuntimeStateService _stateService;
    private readonly IPluginMessageBus _messageBus;
    private readonly PluginLocalizer _localizer;
    private readonly Timer _timer;
    private DateTimeOffset _currentTime = DateTimeOffset.Now;
    private int _disposed;

    public SamplePluginClockService(
        string dataDirectory,
        SamplePluginRuntimeStateService stateService,
        IPluginMessageBus messageBus,
        PluginLocalizer localizer)
    {
        _clockStateFilePath = Path.Combine(dataDirectory, "clock-service.txt");
        _stateService = stateService;
        _messageBus = messageBus;
        _localizer = localizer;
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
            _stateService.MarkClockServiceFaulted(_localizer.Format(
                "status.service.detail.write_failed",
                "时钟状态写入失败：{0}",
                ex.Message));
        }
    }
}
