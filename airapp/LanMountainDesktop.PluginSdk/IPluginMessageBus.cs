namespace LanMountainDesktop.PluginSdk;

public interface IPluginMessageBus
{
    IDisposable Subscribe<TMessage>(Action<TMessage> handler);

    void Publish<TMessage>(TMessage message);
}
