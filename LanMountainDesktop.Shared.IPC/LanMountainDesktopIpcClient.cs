using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using dotnetCampus.Ipc.IpcRouteds.DirectRouteds;
using dotnetCampus.Ipc.Pipes;

namespace LanMountainDesktop.Shared.IPC;

public sealed class LanMountainDesktopIpcClient : IDisposable
{
    private bool _started;

    public LanMountainDesktopIpcClient(string? clientPipeName = null)
    {
        Provider = string.IsNullOrWhiteSpace(clientPipeName)
            ? new IpcProvider()
            : new IpcProvider(clientPipeName);
        RoutedProvider = new JsonIpcDirectRoutedProvider(Provider);
    }

    public IpcProvider Provider { get; }

    public JsonIpcDirectRoutedProvider RoutedProvider { get; }

    public PeerProxy? Peer { get; private set; }

    public bool IsConnected => Peer is not null && Peer.IsConnectedFinished;

    public async Task ConnectAsync(string pipeName = IpcConstants.DefaultPipeName)
    {
        EnsureStarted();
        Peer = await Provider.GetAndConnectToPeerAsync(pipeName).ConfigureAwait(false);
    }

    public void RegisterNotifyHandler<TPayload>(string notifyId, Action<TPayload> handler)
        where TPayload : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notifyId);
        ArgumentNullException.ThrowIfNull(handler);
        RoutedProvider.AddNotifyHandler(notifyId, handler);
    }

    public void RegisterNotifyHandler<TPayload>(string notifyId, Func<TPayload, Task> handler)
        where TPayload : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notifyId);
        ArgumentNullException.ThrowIfNull(handler);
        RoutedProvider.AddNotifyHandler(notifyId, handler);
    }

    public TContract CreateProxy<TContract>(string? objectId = null)
        where TContract : class
    {
        var peer = Peer ?? throw new InvalidOperationException("IPC client is not connected.");
        return Provider.CreateIpcProxy<TContract>(peer, objectId);
    }

    public async Task<PublicIpcCatalogSnapshot?> GetCatalogAsync()
    {
        var client = await GetRoutedClientAsync().ConfigureAwait(false);
        return await client.GetResponseAsync<PublicIpcCatalogSnapshot>(IpcConstants.Routes.CatalogGet)
            .ConfigureAwait(false);
    }

    public async Task<PublicIpcSessionInfo?> GetSessionInfoAsync()
    {
        var client = await GetRoutedClientAsync().ConfigureAwait(false);
        return await client.GetResponseAsync<PublicIpcSessionInfo>(IpcConstants.Routes.SessionGetInfo)
            .ConfigureAwait(false);
    }

    private async Task<JsonIpcDirectRoutedClientProxy> GetRoutedClientAsync()
    {
        if (Peer is null)
        {
            throw new InvalidOperationException("IPC client is not connected.");
        }

        await Task.CompletedTask;
        return new JsonIpcDirectRoutedClientProxy(Peer);
    }

    private void EnsureStarted()
    {
        if (_started)
        {
            return;
        }

        RoutedProvider.StartServer();
        _started = true;
    }

    public void Dispose()
    {
        Provider.Dispose();
    }
}
