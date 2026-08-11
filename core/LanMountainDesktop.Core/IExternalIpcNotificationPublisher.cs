namespace LanMountainDesktop.Shared.IPC;

public interface IExternalIpcNotificationPublisher
{
    Task NotifyAsync<TPayload>(string notifyId, TPayload payload, CancellationToken cancellationToken = default)
        where TPayload : class;
}
