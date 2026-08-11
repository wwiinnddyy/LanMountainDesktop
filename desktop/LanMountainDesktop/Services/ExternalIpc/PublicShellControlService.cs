using Avalonia;
using Avalonia.Threading;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Services.ExternalIpc;

internal sealed class PublicShellControlService : IPublicShellControlService
{
    public Task<PublicShellStatus> GetShellStatusAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            return (Application.Current as App)?.GetPublicShellStatus()
                ?? CreateUnavailableStatus();
        }).GetTask();
    }

    public Task<bool> ActivateMainWindowAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            return (Application.Current as App)?.TryActivateMainWindowFromExternalIpc("PublicIpc") == true;
        }).GetTask();
    }

    public Task<PublicShellActivationResult> ActivateMainWindowWithStatusAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            return (Application.Current as App)?.TryActivateMainWindowWithStatusFromExternalIpc("PublicIpc")
                ?? new PublicShellActivationResult(
                    false,
                    "app_unavailable",
                    "Application instance is not available.",
                    CreateUnavailableStatus());
        }).GetTask();
    }

    public Task<PublicTrayStatus> EnsureTrayReadyAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            return (Application.Current as App)?.EnsureTrayReadyFromExternalIpc("PublicIpc")
                ?? new PublicTrayStatus("Unavailable", false, false, false, false, 0);
        }).GetTask();
    }

    public Task<PublicTaskbarStatus> EnsureTaskbarEntryAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            return (Application.Current as App)?.EnsureTaskbarEntryFromExternalIpc("PublicIpc")
                ?? new PublicTaskbarStatus(false, false, false, false, false, false);
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

    private static PublicShellStatus CreateUnavailableStatus()
    {
        return new PublicShellStatus(
            Environment.ProcessId,
            DateTimeOffset.UtcNow,
            "unknown",
            "Unavailable",
            false,
            false,
            false,
            false,
            false,
            new PublicTrayStatus("Unavailable", false, false, false, false, 0),
            new PublicTaskbarStatus(false, false, false, false, false, false));
    }
}
