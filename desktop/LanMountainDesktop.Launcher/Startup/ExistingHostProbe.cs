using LanMountainDesktop.Launcher.Shell;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Startup;

internal static class ExistingHostProbe
{
    public static MultiInstanceLaunchBehavior LoadMultiInstanceLaunchBehavior(DataLocationResolver dataLocationResolver)
    {
        try
        {
            var settingsPath = HostAppSettingsOobeMerger.GetSettingsFilePath(dataLocationResolver.ResolveDataRoot());
            return HostAppSettingsOobeMerger.LoadMultiInstanceLaunchBehavior(settingsPath);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load multi-instance launch behavior. Falling back to default. {ex.Message}");
            return MultiInstanceLaunchBehavior.NotifyAndOpenDesktop;
        }
    }

    public static async Task<PublicShellStatus?> TryGetExistingHostStatusAsync(
        LanMountainDesktopIpcClient ipcClient,
        TimeSpan timeout)
    {
        try
        {
            var connected = ipcClient.IsConnected ||
                            await PublicIpcConnection.TryConnectAsync(ipcClient, timeout).ConfigureAwait(false);
            if (!connected)
            {
                return null;
            }

            var shellProxy = ipcClient.CreateProxy<IPublicShellControlService>();
            return await shellProxy.GetShellStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Info($"Existing host status probe did not complete: {ex.Message}");
            return null;
        }
    }

    public static async Task<ExistingHostBehaviorResult> ApplyExistingHostBehaviorAsync(
        LanMountainDesktopIpcClient ipcClient,
        MultiInstanceLaunchBehavior behavior,
        PublicShellStatus status)
    {
        try
        {
            var shellProxy = ipcClient.CreateProxy<IPublicShellControlService>();
            return behavior switch
            {
                MultiInstanceLaunchBehavior.OpenDesktopSilently => await ActivateExistingHostForBehaviorAsync(
                    shellProxy,
                    showLauncherNotice: false,
                    successCode: "existing_host_activated",
                    successMessage: "Launcher activated the existing desktop instance.",
                    failureCode: "existing_host_activation_failed").ConfigureAwait(false),

                MultiInstanceLaunchBehavior.NotifyAndOpenDesktop => await ActivateExistingHostForBehaviorAsync(
                    shellProxy,
                    showLauncherNotice: true,
                    successCode: "existing_host_activated_with_notice",
                    successMessage: "Launcher activated the existing desktop instance and showed the repeated-launch notice.",
                    failureCode: "existing_host_activation_failed").ConfigureAwait(false),

                MultiInstanceLaunchBehavior.PromptOnly => await ShowPromptOnlyExistingHostAsync(
                    shellProxy,
                    status).ConfigureAwait(false),

                MultiInstanceLaunchBehavior.RestartApp => await RestartExistingHostAsync(shellProxy).ConfigureAwait(false),

                _ => await ActivateExistingHostForBehaviorAsync(
                    shellProxy,
                    showLauncherNotice: true,
                    successCode: "existing_host_activated_with_notice",
                    successMessage: "Launcher activated the existing desktop instance and showed the repeated-launch notice.",
                    failureCode: "existing_host_activation_failed").ConfigureAwait(false)
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to apply multi-instance behavior '{behavior}': {ex.Message}");
            return new ExistingHostBehaviorResult(
                false,
                "multi_instance_behavior_failed",
                $"Failed to apply multi-instance behavior '{behavior}': {ex.Message}",
                null);
        }
    }

    private static async Task<ExistingHostBehaviorResult> ActivateExistingHostForBehaviorAsync(
        IPublicShellControlService shellProxy,
        bool showLauncherNotice,
        string successCode,
        string successMessage,
        string failureCode)
    {
        var activation = await shellProxy.ActivateMainWindowWithStatusAsync().ConfigureAwait(false);
        var success = activation.Accepted || HostActivationPolicy.IsRecoverableActivationFailure(activation);
        if (showLauncherNotice && success)
        {
            var promptResult = await LaunchUiPresenter.ShowMultiInstancePromptAsync(activation.Status).ConfigureAwait(false);
            if (promptResult == MultiInstancePromptResult.OpenDesktop)
            {
                activation = await shellProxy.ActivateMainWindowWithStatusAsync().ConfigureAwait(false);
            }
        }

        return new ExistingHostBehaviorResult(
            success,
            activation.Accepted ? successCode : success ? "existing_host_startup_pending" : failureCode,
            activation.Accepted ? successMessage : activation.Message,
            activation);
    }

    private static async Task<ExistingHostBehaviorResult> RestartExistingHostAsync(
        IPublicShellControlService shellProxy)
    {
        var accepted = await shellProxy.RestartAsync().ConfigureAwait(false);
        return new ExistingHostBehaviorResult(
            accepted,
            accepted ? "existing_host_restart_requested" : "existing_host_restart_failed",
            accepted
                ? "Launcher requested the existing desktop instance to restart."
                : "Launcher could not request restart from the existing desktop instance.",
            null);
    }

    private static async Task<ExistingHostBehaviorResult> ShowPromptOnlyExistingHostAsync(
        IPublicShellControlService shellProxy,
        PublicShellStatus status)
    {
        var promptResult = await LaunchUiPresenter.ShowMultiInstancePromptAsync(status).ConfigureAwait(false);

        if (promptResult == MultiInstancePromptResult.OpenDesktop)
        {
            return await ActivateExistingHostForBehaviorAsync(
                shellProxy,
                showLauncherNotice: false,
                successCode: "existing_host_activated_from_prompt",
                successMessage: "Launcher activated the existing desktop instance from the prompt.",
                failureCode: "existing_host_activation_failed").ConfigureAwait(false);
        }

        return new ExistingHostBehaviorResult(
            true,
            "existing_host_prompt_only",
            "Launcher showed the repeated-launch prompt and did not open the desktop automatically.",
            null);
    }
}

internal sealed record ExistingHostBehaviorResult(
    bool Success,
    string Code,
    string Message,
    PublicShellActivationResult? ActivationResult);
