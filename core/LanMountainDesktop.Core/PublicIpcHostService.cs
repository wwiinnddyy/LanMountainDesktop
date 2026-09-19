using System.Reflection;
using System.Collections.Concurrent;
using dotnetCampus.Ipc.Context;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using dotnetCampus.Ipc.IpcRouteds.DirectRouteds;
using dotnetCampus.Ipc.Pipes;

namespace LanMountainDesktop.Shared.IPC;

public sealed class PublicIpcHostService : IDisposable, IExternalIpcNotificationPublisher
{
    private static readonly MethodInfo CreateIpcJointMethod = typeof(GeneratedIpcFactory)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == nameof(GeneratedIpcFactory.CreateIpcJoint) &&
            method.IsGenericMethodDefinition &&
            method.GetParameters().Length == 3);

    private readonly Dictionary<(Type ContractType, string ObjectId), PublicServiceEntry> _services = new();
    private readonly ConcurrentDictionary<string, PeerProxy> _connectedPeers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _started;
    private bool _disposed;

    public PublicIpcHostService(string pipeName = IpcConstants.DefaultPipeName)
    {
        PipeName = pipeName;
        StartedAt = DateTimeOffset.UtcNow;
        Provider = new IpcProvider(pipeName);
        RoutedProvider = new JsonIpcDirectRoutedProvider(Provider);
    }

    public string PipeName { get; }

    public DateTimeOffset StartedAt { get; }

    public IpcProvider Provider { get; }

    public JsonIpcDirectRoutedProvider RoutedProvider { get; }

    public Func<IReadOnlyList<PublicAirAppDescriptor>> AirAppDescriptorProvider { get; set; } =
        static () => Array.Empty<PublicAirAppDescriptor>();

    public void Start()
    {
        if (_started)
        {
            return;
        }

        RoutedProvider.AddRequestHandler(IpcConstants.Routes.SessionGetInfo, () => BuildSessionInfo());
        RoutedProvider.AddRequestHandler(IpcConstants.Routes.CatalogGet, () => GetCatalogSnapshot());
        Provider.PeerConnected += OnPeerConnected;
        RoutedProvider.StartServer();
        _started = true;
    }

    public void RegisterPublicService<TContract>(
        TContract implementation,
        string? objectId = null,
        string? airAppId = null,
        params string[] notifyIds)
        where TContract : class
    {
        RegisterPublicService(typeof(TContract), implementation, objectId, airAppId, notifyIds);
    }

    public void RegisterPublicService(
        Type contractType,
        object implementation,
        string? objectId = null,
        string? airAppId = null,
        IEnumerable<string>? notifyIds = null)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        ArgumentNullException.ThrowIfNull(implementation);

        var normalizedObjectId = objectId ?? string.Empty;
        var normalizedNotifyIds = notifyIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        lock (_gate)
        {
            if (_services.ContainsKey((contractType, normalizedObjectId)))
            {
                throw new InvalidOperationException(
                    $"Public IPC contract '{contractType.FullName}' with object id '{normalizedObjectId}' is already registered.");
            }

            CreateIpcJointMethod
                .MakeGenericMethod(contractType)
                .Invoke(null, [Provider, implementation, string.IsNullOrEmpty(normalizedObjectId) ? null : normalizedObjectId]);

            _services[(contractType, normalizedObjectId)] = new PublicServiceEntry(
                contractType,
                implementation,
                string.IsNullOrEmpty(normalizedObjectId) ? null : normalizedObjectId,
                airAppId,
                normalizedNotifyIds);
        }

        if (_started)
        {
            _ = NotifyCatalogChangedAsync();
        }
    }

    public PublicIpcCatalogSnapshot GetCatalogSnapshot()
    {
        PublicIpcServiceDescriptor[] services;
        lock (_gate)
        {
            services = _services.Values
                .Select(entry => new PublicIpcServiceDescriptor(
                    entry.ContractType.FullName ?? entry.ContractType.Name,
                    entry.ContractType.Assembly.GetName().Name ?? string.Empty,
                    entry.ContractType.AssemblyQualifiedName,
                    entry.ObjectId,
                    entry.PluginId,
                    string.IsNullOrWhiteSpace(entry.PluginId),
                    entry.NotifyIds))
                .OrderBy(entry => entry.PluginId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.ContractTypeName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var airApps = AirAppDescriptorProvider()?.ToArray() ?? Array.Empty<PublicAirAppDescriptor>();
        return new PublicIpcCatalogSnapshot(services, airApps, DateTimeOffset.UtcNow);
    }

    public Task PublishStartupProgressAsync(
        LanMountainDesktop.Shared.Contracts.Launcher.StartupProgressMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return NotifyAsync(IpcRoutedNotifyIds.LauncherStartupProgress, message, cancellationToken);
    }

    public Task PublishLoadingStateAsync(
        LanMountainDesktop.Shared.Contracts.Launcher.LoadingStateMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return NotifyAsync(IpcRoutedNotifyIds.LauncherLoadingState, message, cancellationToken);
    }

    public async Task NotifyAsync<TPayload>(string notifyId, TPayload payload, CancellationToken cancellationToken = default)
        where TPayload : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notifyId);
        ArgumentNullException.ThrowIfNull(payload);

        cancellationToken.ThrowIfCancellationRequested();
        foreach (var peer in _connectedPeers.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var client = new JsonIpcDirectRoutedClientProxy(peer);
                var notifyTask = client.NotifyAsync(notifyId, payload);
                var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
                var completed = await Task.WhenAny(notifyTask, timeoutTask).ConfigureAwait(false);
                if (completed == timeoutTask)
                {
                    // Per-peer timeout: don't block startup on a stale launcher peer
                    continue;
                }
                await notifyTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Keep notification fan-out best-effort. Broken peers are cleaned by dotnetCampus.Ipc.
            }
        }
    }

    private Task NotifyCatalogChangedAsync()
    {
        return NotifyAsync(IpcRoutedNotifyIds.CatalogChanged, GetCatalogSnapshot());
    }

    private PublicIpcSessionInfo BuildSessionInfo()
    {
        return new PublicIpcSessionInfo(
            PipeName,
            IpcConstants.ProtocolVersion,
            [
                IpcConstants.Routes.SessionGetInfo,
                IpcConstants.Routes.CatalogGet,
                IpcRoutedNotifyIds.CatalogChanged,
                IpcRoutedNotifyIds.LauncherStartupProgress,
                IpcRoutedNotifyIds.LauncherLoadingState
            ],
            StartedAt);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 每个 peer 的 PeerConnectionBroken 是用 this 作接收者的实例方法，不退订就会让已释放的
        // 宿主服务被库持有的 peer 反向引用住。必须在 Clear() 之前做，否则就没有 peer 可枚举了。
        foreach (var peer in _connectedPeers.Values)
        {
            peer.PeerConnectionBroken -= OnPeerConnectionBroken;
        }

        _connectedPeers.Clear();

        Provider.PeerConnected -= OnPeerConnected;
        Provider.Dispose();
    }

    private void OnPeerConnected(object? sender, PeerConnectedArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var peer = e.Peer;
        _connectedPeers[peer.PeerName] = peer;
        peer.PeerConnectionBroken -= OnPeerConnectionBroken;
        peer.PeerConnectionBroken += OnPeerConnectionBroken;
    }

    private void OnPeerConnectionBroken(object? sender, IPeerConnectionBrokenArgs e)
    {
        if (sender is PeerProxy peer)
        {
            _connectedPeers.TryRemove(peer.PeerName, out _);
        }
    }

    private sealed record PublicServiceEntry(
        Type ContractType,
        object Implementation,
        string? ObjectId,
        string? PluginId,
        string[] NotifyIds);
}
