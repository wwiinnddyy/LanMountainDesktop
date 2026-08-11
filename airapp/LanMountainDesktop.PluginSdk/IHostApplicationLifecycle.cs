namespace LanMountainDesktop.PluginSdk;

public sealed record HostApplicationLifecycleRequest(
    string? Source = null,
    string? Reason = null);

public interface IHostApplicationLifecycle
{
    bool TryExit(HostApplicationLifecycleRequest? request = null);

    bool TryRestart(HostApplicationLifecycleRequest? request = null);
}
