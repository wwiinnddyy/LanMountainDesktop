using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Resources;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher.Shell;

internal static class ApplyUpdateGuiFlow
{
    public static async Task RunAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        CommandContext context,
        UpdateWindow window)
    {
        var appRoot = Commands.ResolveAppRoot(context);
        var deploymentLocator = new DeploymentLocator(appRoot);
        var updateEngine = UpdateEngineFactory.Create(deploymentLocator);
        var pluginInstaller = new PluginInstallerService();
        var pluginUpgrades = new PluginUpgradeQueueService(pluginInstaller);

        var success = true;
        string? errorMessage = null;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => window.Report("verify", Strings.Update_Verifying, 10));
            var updateResult = await updateEngine.ApplyPendingUpdateAsync().ConfigureAwait(false);
            if (!updateResult.Success)
            {
                success = false;
                errorMessage = updateResult.Message;
            }

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() => window.Report("plugins", Strings.Update_ApplyingPlugins, 60));
                var pluginsDir = context.GetOption("plugins-dir") ?? Path.Combine(appRoot, "plugins");
                var queueResult = pluginUpgrades.ApplyPendingUpgrades(pluginsDir);
                if (!queueResult.Success && queueResult.Code != "noop")
                {
                    Logger.Error($"Plugin upgrade failed during apply-update: {queueResult.Message}");
                }
            }

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() => window.Report("cleanup", Strings.Update_CleaningUp, 90));
                deploymentLocator.CleanupOldDeployments(minVersionsToKeep: 3);
            }
        }
        catch (Exception ex)
        {
            success = false;
            errorMessage = ex.Message;
            Logger.Error("Apply-update flow failed.", ex);
        }

        await Dispatcher.UIThread.InvokeAsync(() => window.ReportComplete(success, errorMessage));
        await Task.Delay(success ? 1500 : 5000).ConfigureAwait(false);

        await Commands.WriteResultIfNeededAsync(context.GetOption("result"), new LauncherResult
        {
            Success = success,
            Stage = "apply-update",
            Code = success ? "ok" : "failed",
            Message = success ? "Update applied successfully." : (errorMessage ?? "Unknown error"),
            Details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["command"] = context.Command,
                ["launchSource"] = context.LaunchSource
            }
        }).ConfigureAwait(false);

        Environment.ExitCode = success ? 0 : 1;
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(Environment.ExitCode), DispatcherPriority.Background);
    }
}
