using System.Diagnostics;
using System.Runtime.InteropServices;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.AirAppRuntime;

internal sealed class AirAppLifecycleService : IAirAppLifecycleService
{
    private readonly object _gate = new();
    private readonly IAirAppProcessStarter _processStarter;
    private readonly Dictionary<string, ManagedAirAppInstance> _instances = new(StringComparer.OrdinalIgnoreCase);

    public AirAppLifecycleService(IAirAppProcessStarter processStarter)
    {
        _processStarter = processStarter;
    }

    public Task<AirAppOperationResult> OpenAsync(AirAppOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var appId = Normalize(request.AppId, "unknown");
        var instanceKey = AirAppInstanceKey.Build(appId, request.SourceComponentId, request.SourcePlacementId);
        AirAppRuntimeLogger.Info(
            $"Air APP open requested. AppId='{appId}'; InstanceKey='{instanceKey}'; RequesterProcessId={request.RequesterProcessId}.");

        lock (_gate)
        {
            CleanupExitedInstances();

            if (_instances.TryGetValue(instanceKey, out var existing) && IsProcessAlive(existing.ProcessId))
            {
                TryActivateProcess(existing.ProcessId);
                existing.Touch();
                return Task.FromResult(BuildResult(true, "activated_existing", "Activated existing Air APP instance.", existing));
            }

            var sessionId = Guid.NewGuid().ToString("N");
            try
            {
                var process = _processStarter.Start(
                    appId,
                    sessionId,
                    instanceKey,
                    request.SourceComponentId,
                    request.SourcePlacementId);
                if (process is null)
                {
                    return Task.FromResult(BuildResult(false, "start_failed", "AirAppHost process was not created.", null));
                }

                var instance = new ManagedAirAppInstance(
                    instanceKey,
                    appId,
                    sessionId,
                    process.Id,
                    $"{appId} - Air APP",
                    request.SourceComponentId,
                    request.SourcePlacementId);
                _instances[instanceKey] = instance;
                AirAppRuntimeLogger.Info($"Started Air APP. AppId='{appId}'; InstanceKey='{instanceKey}'; ProcessId={process.Id}.");
                return Task.FromResult(BuildResult(true, "started", "Started Air APP instance.", instance));
            }
            catch (Exception ex)
            {
                AirAppRuntimeLogger.Warn($"Failed to start Air APP '{appId}': {ex.Message}");
                return Task.FromResult(BuildResult(false, "start_failed", ex.Message, null));
            }
        }
    }

    public Task<AirAppOperationResult> ActivateAsync(string instanceKey)
    {
        lock (_gate)
        {
            CleanupExitedInstances();
            if (!_instances.TryGetValue(instanceKey, out var instance))
            {
                return Task.FromResult(BuildResult(false, "not_found", "Air APP instance was not found.", null));
            }

            var accepted = TryActivateProcess(instance.ProcessId);
            instance.Touch();
            return Task.FromResult(BuildResult(
                accepted,
                accepted ? "activated" : "activation_failed",
                accepted ? "Air APP instance activated." : "Failed to activate Air APP instance.",
                instance));
        }
    }

    public Task<AirAppOperationResult> CloseAsync(string instanceKey)
    {
        lock (_gate)
        {
            CleanupExitedInstances();
            if (!_instances.TryGetValue(instanceKey, out var instance))
            {
                return Task.FromResult(BuildResult(false, "not_found", "Air APP instance was not found.", null));
            }

            var accepted = TryCloseProcess(instance.ProcessId);
            instance.Touch();
            return Task.FromResult(BuildResult(
                accepted,
                accepted ? "close_requested" : "close_failed",
                accepted ? "Air APP close requested." : "Failed to request Air APP close.",
                instance));
        }
    }

    public Task<AirAppInstanceInfo[]> GetInstancesAsync()
    {
        lock (_gate)
        {
            CleanupExitedInstances();
            return Task.FromResult(_instances.Values.Select(static instance => instance.ToInfo()).ToArray());
        }
    }

    public Task<AirAppOperationResult> RegisterAsync(AirAppRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var instanceKey = string.IsNullOrWhiteSpace(request.InstanceKey)
                ? AirAppInstanceKey.Build(request.AppId, request.SourceComponentId, request.SourcePlacementId)
                : request.InstanceKey.Trim();
            var instance = new ManagedAirAppInstance(
                instanceKey,
                Normalize(request.AppId, "unknown"),
                Normalize(request.SessionId, Guid.NewGuid().ToString("N")),
                request.ProcessId,
                Normalize(request.WindowTitle, $"{request.AppId} - Air APP"),
                request.SourceComponentId,
                request.SourcePlacementId);
            _instances[instanceKey] = instance;
            AirAppRuntimeLogger.Info($"Registered Air APP. AppId='{instance.AppId}'; InstanceKey='{instanceKey}'; ProcessId={instance.ProcessId}.");
            return Task.FromResult(BuildResult(true, "registered", "Air APP instance registered.", instance));
        }
    }

    public Task<AirAppOperationResult> UnregisterAsync(string instanceKey, int processId)
    {
        lock (_gate)
        {
            if (_instances.TryGetValue(instanceKey, out var instance) &&
                (processId <= 0 || instance.ProcessId == processId))
            {
                _instances.Remove(instanceKey);
                AirAppRuntimeLogger.Info($"Unregistered Air APP. InstanceKey='{instanceKey}'; ProcessId={processId}.");
                return Task.FromResult(BuildResult(true, "unregistered", "Air APP instance unregistered.", instance));
            }

            return Task.FromResult(BuildResult(false, "not_found", "Air APP instance was not found.", null));
        }
    }

    public bool HasLiveAirApps()
    {
        lock (_gate)
        {
            CleanupExitedInstances();
            return _instances.Values.Any(static instance => IsProcessAlive(instance.ProcessId));
        }
    }

    private void CleanupExitedInstances()
    {
        var exitedKeys = _instances
            .Where(static pair => !IsProcessAlive(pair.Value.ProcessId))
            .Select(static pair => pair.Key)
            .ToList();

        foreach (var key in exitedKeys)
        {
            _instances.Remove(key);
            AirAppRuntimeLogger.Info($"Pruned exited Air APP instance. InstanceKey='{key}'.");
        }
    }

    private static AirAppOperationResult BuildResult(
        bool accepted,
        string code,
        string message,
        ManagedAirAppInstance? instance)
    {
        return new AirAppOperationResult(accepted, code, message, instance?.ToInfo());
    }

    private static bool TryActivateProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            if (!OperatingSystem.IsWindows())
            {
                return true;
            }

            process.Refresh();
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero)
            {
                return true;
            }

            _ = ShowWindow(handle, SW_SHOWNORMAL);
            _ = SetForegroundWindow(handle);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCloseProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            return process.CloseMainWindow();
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsProcessAlive(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private const int SW_SHOWNORMAL = 1;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private sealed class ManagedAirAppInstance
    {
        private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;

        public ManagedAirAppInstance(
            string instanceKey,
            string appId,
            string sessionId,
            int processId,
            string windowTitle,
            string? sourceComponentId,
            string? sourcePlacementId)
        {
            InstanceKey = instanceKey;
            AppId = appId;
            SessionId = sessionId;
            ProcessId = processId;
            WindowTitle = windowTitle;
            SourceComponentId = sourceComponentId;
            SourcePlacementId = sourcePlacementId;
            UpdatedAtUtc = _startedAtUtc;
        }

        public string InstanceKey { get; }

        public string AppId { get; }

        public string SessionId { get; }

        public int ProcessId { get; }

        public string WindowTitle { get; }

        public string? SourceComponentId { get; }

        public string? SourcePlacementId { get; }

        public DateTimeOffset UpdatedAtUtc { get; private set; }

        public void Touch()
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        public AirAppInstanceInfo ToInfo()
        {
            return new AirAppInstanceInfo(
                InstanceKey,
                AppId,
                SessionId,
                ProcessId,
                WindowTitle,
                SourceComponentId,
                SourcePlacementId,
                IsProcessAlive(ProcessId),
                _startedAtUtc,
                UpdatedAtUtc);
        }
    }
}
