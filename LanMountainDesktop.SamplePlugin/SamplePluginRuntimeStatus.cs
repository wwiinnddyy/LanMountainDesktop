using System.Globalization;
using System.IO;
using System.Threading;

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

internal static class SamplePluginRuntimeStatus
{
    private static readonly object Gate = new();

    private static SamplePluginStatusEntry _frontend = CreateEntry(
        "frontend",
        "Frontend",
        SamplePluginHealthState.Pending,
        "Pending",
        "Frontend surfaces have not been created yet.");

    private static SamplePluginStatusEntry _component = CreateEntry(
        "component",
        "Component",
        SamplePluginHealthState.Pending,
        "Pending",
        "The 4x4 component has not been created yet.");

    private static SamplePluginStatusEntry _backend = CreateEntry(
        "backend",
        "Backend",
        SamplePluginHealthState.Pending,
        "Pending",
        "Plugin initialization has not finished yet.");

    private static SamplePluginStatusEntry _service = CreateEntry(
        "service",
        "Service",
        SamplePluginHealthState.Pending,
        "Pending",
        "Heartbeat service has not started yet.");

    public static void Reset(string hostName, string version, string dataDirectory)
    {
        lock (Gate)
        {
            _frontend = CreateEntry(
                "frontend",
                "Frontend",
                SamplePluginHealthState.Pending,
                "Pending",
                "Waiting for the settings page or widget surface to render.");

            _component = CreateEntry(
                "component",
                "Component",
                SamplePluginHealthState.Pending,
                "Pending",
                "The 4x4 component has not been created yet.");

            _backend = CreateEntry(
                "backend",
                "Backend",
                SamplePluginHealthState.Healthy,
                "Healthy",
                $"Plugin initialized. Host: {hostName}; Version: {version}; Data: {dataDirectory}");

            _service = CreateEntry(
                "service",
                "Service",
                SamplePluginHealthState.Pending,
                "Pending",
                "Heartbeat service is starting.");
        }
    }

    public static void MarkFrontendReady(string detail)
    {
        lock (Gate)
        {
            _frontend = CreateEntry(
                "frontend",
                "Frontend",
                SamplePluginHealthState.Healthy,
                "Healthy",
                detail);
        }
    }

    public static void MarkComponentCreated(string detail)
    {
        lock (Gate)
        {
            _component = CreateEntry(
                "component",
                "Component",
                SamplePluginHealthState.Healthy,
                "Created",
                detail);
        }
    }

    public static void MarkBackendReady(string detail)
    {
        lock (Gate)
        {
            _backend = CreateEntry(
                "backend",
                "Backend",
                SamplePluginHealthState.Healthy,
                "Healthy",
                detail);
        }
    }

    public static void MarkBackendFaulted(string detail)
    {
        lock (Gate)
        {
            _backend = CreateEntry(
                "backend",
                "Backend",
                SamplePluginHealthState.Faulted,
                "Faulted",
                detail);
        }
    }

    public static void MarkServiceHeartbeat(DateTimeOffset timestamp)
    {
        lock (Gate)
        {
            _service = CreateEntry(
                "service",
                "Service",
                SamplePluginHealthState.Healthy,
                "Healthy",
                $"Heartbeat service is running. Last heartbeat: {timestamp.LocalDateTime:HH:mm:ss}");
        }
    }

    public static void MarkServiceFaulted(string detail)
    {
        lock (Gate)
        {
            _service = CreateEntry(
                "service",
                "Service",
                SamplePluginHealthState.Faulted,
                "Faulted",
                detail);
        }
    }

    public static IReadOnlyList<SamplePluginStatusEntry> GetSnapshot()
    {
        lock (Gate)
        {
            return
            [
                _frontend,
                _component,
                _backend,
                _service
            ];
        }
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

internal sealed class SamplePluginHeartbeatService : IDisposable
{
    private readonly string _heartbeatFilePath;
    private readonly Timer _timer;
    private int _disposed;

    public SamplePluginHeartbeatService(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _heartbeatFilePath = Path.Combine(dataDirectory, "service-heartbeat.txt");
        _timer = new Timer(OnTimerTick);
    }

    public void Start()
    {
        PublishHeartbeat();
        _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
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
        PublishHeartbeat();
    }

    private void PublishHeartbeat()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        try
        {
            File.WriteAllText(
                _heartbeatFilePath,
                now.ToString("O", CultureInfo.InvariantCulture));
            SamplePluginRuntimeStatus.MarkServiceHeartbeat(now);
        }
        catch (Exception ex)
        {
            SamplePluginRuntimeStatus.MarkServiceFaulted($"Heartbeat write failed: {ex.Message}");
        }
    }
}
