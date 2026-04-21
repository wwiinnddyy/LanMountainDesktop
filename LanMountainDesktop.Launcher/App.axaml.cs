using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher;

public partial class App : Application
{
    public override void Initialize()
    {
        Logger.Initialize();
        var context = LauncherRuntimeContext.Current;
        Logger.Info(
            $"Launcher App initialize. Command='{context.Command}'; IsGuiMode={context.IsGuiCommand}; " +
            $"IsPreview={context.IsPreviewCommand}; IsDebugMode={context.IsDebugMode}; " +
            $"ExplicitAppRoot='{context.ExplicitAppRoot ?? "<none>"}'.");

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var context = LauncherRuntimeContext.Current;
            Logger.Info(
                $"Framework initialization completed. Command='{context.Command}'; IsPreview={context.IsPreviewCommand}; " +
                $"IsDebugMode={context.IsDebugMode}.");

            if (HandlePreviewCommand(context, desktop))
            {
                base.OnFrameworkInitializationCompleted();
                return;
            }

            if (string.Equals(context.Command, "apply-update", StringComparison.OrdinalIgnoreCase))
            {
                var updateWindow = new UpdateWindow();
                updateWindow.Show();
                _ = RunApplyUpdateWithWindowAsync(desktop, context, updateWindow);
            }
            else
            {
                var splashWindow = new SplashWindow();
                splashWindow.Show();
                _ = RunCoordinatorWithSplashAsync(desktop, context, splashWindow);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private bool HandlePreviewCommand(CommandContext context, IClassicDesktopStyleApplicationLifetime desktop)
    {
        switch (context.Command.ToLowerInvariant())
        {
            case "preview-splash":
            {
                Logger.Info("Preview command: splash.");
                var splashWindow = new SplashWindow();
                splashWindow.SetDebugMode(true);
                splashWindow.Show();
                _ = SimulateSplashPreviewAsync(desktop, splashWindow);
                return true;
            }
            case "preview-error":
            {
                Logger.Info("Preview command: error.");
                var errorWindow = new ErrorWindow();
                errorWindow.SetErrorMessage("[Preview] This is the launcher error window preview.");
                errorWindow.Show();
                _ = WaitForWindowCloseAsync(desktop, errorWindow);
                return true;
            }
            case "preview-update":
            {
                Logger.Info("Preview command: update.");
                var updateWindow = new UpdateWindow();
                updateWindow.SetDebugMode(true);
                updateWindow.Show();
                _ = SimulateUpdatePreviewAsync(desktop, updateWindow);
                return true;
            }
            case "preview-oobe":
            {
                Logger.Info("Preview command: oobe.");
                var oobeWindow = new OobeWindow();
                oobeWindow.Show();
                _ = SimulateOobePreviewAsync(desktop, oobeWindow);
                return true;
            }
            case "preview-debug":
            {
                Logger.Info("Preview command: debug window.");
                var devDebugWindow = new DevDebugWindow();
                devDebugWindow.Show();
                return true;
            }
            default:
                return false;
        }
    }

    private async Task SimulateSplashPreviewAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow window)
    {
        var stages = new[] { "initializing", "update", "plugins", "launch", "ready" };
        var messages = new[] { "Initializing...", "Checking updates...", "Checking plugins...", "Launching host...", "Ready" };
        var reporter = (ISplashStageReporter)window;

        for (var i = 0; i < stages.Length; i++)
        {
            reporter.Report(stages[i], messages[i]);
            await Task.Delay(800).ConfigureAwait(false);
        }

        await Task.Delay(5000).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
    }

    private async Task SimulateUpdatePreviewAsync(IClassicDesktopStyleApplicationLifetime desktop, UpdateWindow window)
    {
        var stages = new[] { "verify", "extract", "apply", "plugins", "cleanup" };

        for (var i = 0; i < stages.Length; i++)
        {
            window.Report(stages[i], $"Processing {stages[i]}...", (i + 1) * 20);
            await Task.Delay(600).ConfigureAwait(false);
        }

        window.ReportComplete(true, null);
        await Task.Delay(3000).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
    }

    private async Task SimulateOobePreviewAsync(IClassicDesktopStyleApplicationLifetime desktop, OobeWindow window)
    {
        try
        {
            await window.WaitForEnterAsync().ConfigureAwait(false);
            Logger.Info("OOBE preview completed by user.");
        }
        catch (Exception ex)
        {
            Logger.Error("OOBE preview failed.", ex);
        }

        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
    }

    private async Task WaitForWindowCloseAsync(IClassicDesktopStyleApplicationLifetime desktop, Window window)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => tcs.TrySetResult();
        await tcs.Task.ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
    }

    private static async Task RunCoordinatorWithSplashAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        CommandContext context,
        SplashWindow splashWindow)
    {
        LauncherResult result;

        try
        {
            var appRoot = Commands.ResolveAppRoot(context);
            Logger.Info(
                $"Coordinator start. Command='{context.Command}'; AppRoot='{appRoot}'; " +
                $"IsDebugMode={context.IsDebugMode}; ResultPath='{context.GetOption("result") ?? "<none>"}'.");

            var deploymentLocator = new DeploymentLocator(appRoot);
            var coordinator = new LauncherFlowCoordinator(
                context,
                deploymentLocator,
                new OobeStateService(appRoot),
                new UpdateEngineService(deploymentLocator),
                new PluginInstallerService());

            result = await coordinator.RunAsync(splashWindow).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("Coordinator threw an unhandled exception.", ex);
            result = new LauncherResult
            {
                Success = false,
                Stage = "launch",
                Code = "exception",
                Message = $"Launcher failed: {ex.Message}",
                ErrorMessage = ex.ToString()
            };
        }

        Logger.Info($"Coordinator completed. Success={result.Success}; Stage='{result.Stage}'; Code='{result.Code}'.");
        await WriteLauncherResultAsync(context, result).ConfigureAwait(false);

        if (!result.Success &&
            result.Code is not "host_not_found" &&
            (string.Equals(result.Stage, "launch", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(result.Stage, "launchHost", StringComparison.OrdinalIgnoreCase)))
        {
            await ShowFailureWindowAsync(result).ConfigureAwait(false);
        }

        Environment.ExitCode = result.Success ? 0 : 1;
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(Environment.ExitCode), DispatcherPriority.Background);
    }

    private static async Task WriteLauncherResultAsync(CommandContext context, LauncherResult result)
    {
        var resultPath = context.GetOption("result");
        if (string.IsNullOrWhiteSpace(resultPath))
        {
            return;
        }

        try
        {
            await Commands.WriteResultIfNeededAsync(resultPath, result).ConfigureAwait(false);
            Logger.Info($"Launcher result written to '{Path.GetFullPath(resultPath)}'.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to write launcher result to '{resultPath}'.", ex);
        }
    }

    private static async Task ShowFailureWindowAsync(LauncherResult result)
    {
        ErrorWindow? errorWindow = null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                errorWindow = new ErrorWindow();
                errorWindow.SetErrorMessage(
                    $"Failed to start LanMountainDesktop.\n\nStage: {result.Stage}\nCode: {result.Code}\n\n{result.Message}");
                errorWindow.Show();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show launcher failure window.", ex);
            }
        });

        if (errorWindow is null)
        {
            return;
        }

        try
        {
            await errorWindow.WaitForChoiceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("Failure window closed unexpectedly.", ex);
        }
    }

    private static async Task RunApplyUpdateWithWindowAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        CommandContext context,
        UpdateWindow window)
    {
        var appRoot = Commands.ResolveAppRoot(context);
        var deploymentLocator = new DeploymentLocator(appRoot);
        var updateEngine = new UpdateEngineService(deploymentLocator);
        var pluginInstaller = new PluginInstallerService();
        var pluginUpgrades = new PluginUpgradeQueueService(pluginInstaller);

        var success = true;
        string? errorMessage = null;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => window.Report("verify", "Verifying update...", 10));
            var updateResult = await updateEngine.ApplyPendingUpdateAsync().ConfigureAwait(false);
            if (!updateResult.Success)
            {
                success = false;
                errorMessage = updateResult.Message;
            }

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() => window.Report("plugins", "Applying plugin upgrades...", 60));
                var pluginsDir = context.GetOption("plugins-dir") ?? Path.Combine(appRoot, "plugins");
                var queueResult = pluginUpgrades.ApplyPendingUpgrades(pluginsDir);
                if (!queueResult.Success && queueResult.Code != "noop")
                {
                    Logger.Error($"Plugin upgrade failed during apply-update: {queueResult.Message}");
                }
            }

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() => window.Report("cleanup", "Cleaning up old deployments...", 90));
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
            Message = success ? "Update applied successfully." : (errorMessage ?? "Unknown error")
        }).ConfigureAwait(false);

        Environment.ExitCode = success ? 0 : 1;
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(Environment.ExitCode), DispatcherPriority.Background);
    }
}
