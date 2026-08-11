namespace LanMountainDesktop.AirAppSdk;

public interface IAirAppPublicIpcBuilder
{
    IAirAppPublicIpcBuilder AddService<TContract>(
        string? objectId = null,
        IEnumerable<string>? notifyIds = null)
        where TContract : class;

    IAirAppPublicIpcBuilder AddService(
        Type contractType,
        object implementation,
        string? objectId = null,
        IEnumerable<string>? notifyIds = null);
}
