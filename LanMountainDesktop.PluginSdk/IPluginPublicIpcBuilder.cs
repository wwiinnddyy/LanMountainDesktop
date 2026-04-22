namespace LanMountainDesktop.PluginSdk;

public interface IPluginPublicIpcBuilder
{
    IPluginPublicIpcBuilder AddService<TContract>(
        string? objectId = null,
        IEnumerable<string>? notifyIds = null)
        where TContract : class;

    IPluginPublicIpcBuilder AddService(
        Type contractType,
        object implementation,
        string? objectId = null,
        IEnumerable<string>? notifyIds = null);
}
