using Avalonia;
using Avalonia.Threading;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Services.ExternalIpc;

internal sealed class PublicShellControlService : IPublicShellControlService
{
    public Task<bool> ActivateMainWindowAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            return (Application.Current as App)?.TryActivateMainWindowFromExternalIpc("PublicIpc") == true;
        }).GetTask();
    }

    public Task<bool> OpenSettingsAsync(string? pageTag = null)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Application.Current is not App app)
            {
                return false;
            }

            app.OpenIndependentSettingsModule("PublicIpc", pageTag);
            return true;
        }).GetTask();
    }

    public Task<bool> RestartAsync()
    {
        var lifecycle = App.CurrentHostApplicationLifecycle;
        return Task.FromResult(lifecycle?.TryRestart(new HostApplicationLifecycleRequest(
            Source: "PublicIpc",
            Reason: "External IPC requested restart.")) == true);
    }

    public Task<bool> ExitAsync()
    {
        var lifecycle = App.CurrentHostApplicationLifecycle;
        return Task.FromResult(lifecycle?.TryExit(new HostApplicationLifecycleRequest(
            Source: "PublicIpc",
            Reason: "External IPC requested exit.")) == true);
    }
}
