using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Launcher.Services.Ipc;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher;

public partial class App : Application
{
    public override void Initialize()
    {
        Logger.Initialize();
        var context = LauncherRuntimeContext.Current;
        var execution = LauncherExecutionContext.Capture();
        Logger.Info(
            $"Launcher App initialize. Command='{context.Command}'; IsGuiMode={context.IsGuiCommand}; " +
            $"IsPreview={context.IsPreviewCommand}; IsDebugMode={context.IsDebugMode}; " +
            $"LaunchSource='{context.LaunchSource}'; IsElevated={execution.IsElevated}; " +
            $"UserSid='{execution.UserSid ?? string.Empty}'; ExplicitAppRoot='{context.ExplicitAppRoot ?? "<none>"}'.");

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var context = LauncherRuntimeContext.Current;
            var execution = LauncherExecutionContext.Capture();
            Logger.Info(
                $"Framework initialization completed. Command='{context.Command}'; IsPreview={context.IsPreviewCommand}; " +
                $"IsDebugMode={context.IsDebugMode}; LaunchSource='{context.LaunchSource}'; " +
                $"IsElevated={execution.IsElevated}; UserSid='{execution.UserSid ?? string.Empty}'.");

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
                var splashWindow = CreateSplashWindow();
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
                var splashWindow = CreateSplashWindow();
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

    private static SplashWindow CreateSplashWindow()
    {
        var preferences = StartupVisualPreferencesResolver.Resolve();
        return new SplashWindow(preferences.Mode);
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
        SplashWindow? currentSplashWindow = splashWindow;
        var appRoot = Commands.ResolveAppRoot(context);
        var startupAttemptRegistry = new StartupAttemptRegistry();
        var coordinatorPipeName = LauncherCoordinatorIpcServer.CreatePipeName();
        var successPolicy = LauncherFlowCoordinator.ResolveSuccessPolicyKey(context);

        if (!startupAttemptRegistry.TryReserveCoordinator(
                context.LaunchSource,
                successPolicy,
                coordinatorPipeName,
                out var reservedAttempt,
                out var activeCoordinatorAttempt))
        {
            result = await AttachToExistingCoordinatorAsync(
                context,
                currentSplashWindow,
                activeCoordinatorAttempt).ConfigureAwait(false);

            Logger.Info($"Secondary launcher completed. Success={result.Success}; Code='{result.Code}'.");
            await WriteLauncherResultAsync(context, result).ConfigureAwait(false);

            Environment.ExitCode = result.Success ? 0 : 1;
            await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(Environment.ExitCode), DispatcherPriority.Background);
            return;
        }

        using var coordinatorServer = new LauncherCoordinatorIpcServer(
            coordinatorPipeName,
            BuildCoordinatorStatusFromAttempt(reservedAttempt),
            HandleCoordinatorRequestAsync,
            startupAttemptRegistry.UpdateOwnedCoordinatorHeartbeat);
        coordinatorServer.Start();

        while (true)
        {
            try
            {
                Logger.Info(
                    $"Coordinator start. Command='{context.Command}'; AppRoot='{appRoot}'; " +
                    $"IsDebugMode={context.IsDebugMode}; LaunchSource='{context.LaunchSource}'; " +
                    $"ResultPath='{context.GetOption("result") ?? "<none>"}'.");

                var deploymentLocator = new DeploymentLocator(appRoot);
                var coordinator = new LauncherFlowCoordinator(
                    context,
                    deploymentLocator,
                    new OobeStateService(appRoot),
                    new UpdateEngineService(deploymentLocator),
                    new PluginInstallerService(),
                    startupAttemptRegistry,
                    coordinatorServer);

                result = await coordinator.RunAsync(currentSplashWindow).ConfigureAwait(false);
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

            if (result.Success ||
                result.Code == "host_not_found" ||
                (!string.Equals(result.Stage, "launch", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(result.Stage, "launchHost", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            var failureAction = await ShowFailureWindowAsync(result).ConfigureAwait(false);
            if (failureAction == ErrorWindowResult.Exit)
            {
                break;
            }

            if (failureAction == ErrorWindowResult.ActivateExisting &&
                await TryActivateExistingInstanceAsync().ConfigureAwait(false))
            {
                result = new LauncherResult
                {
                    Success = true,
                    Stage = "launch",
                    Code = "activation_requested",
                    Message = "Launcher activated the existing desktop instance.",
                    Details = result.Details
                };
                break;
            }

            currentSplashWindow = CreateSplashWindow();
            currentSplashWindow.Show();
        }

        Logger.Info($"Coordinator completed. Success={result.Success}; Stage='{result.Stage}'; Code='{result.Code}'.");
        await WriteLauncherResultAsync(context, result).ConfigureAwait(false);

        Environment.ExitCode = result.Success ? 0 : 1;
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(Environment.ExitCode), DispatcherPriority.Background);
    }

    private static async Task<LauncherResult> AttachToExistingCoordinatorAsync(
        CommandContext context,
        SplashWindow? splashWindow,
        StartupAttemptRecord? activeCoordinatorAttempt)
    {
        var reporter = splashWindow as ISplashStageReporter;
        reporter?.Report("activation", "Connecting to the active launcher...");

        if (activeCoordinatorAttempt is not null &&
            !string.IsNullOrWhiteSpace(activeCoordinatorAttempt.CoordinatorPipeName))
        {
            var command = string.Equals(context.LaunchSource, "restart", StringComparison.OrdinalIgnoreCase)
                ? LauncherCoordinatorCommands.Attach
                : LauncherCoordinatorCommands.ActivateDesktop;
            var request = new LauncherCoordinatorRequest
            {
                Command = command,
                LaunchSource = context.LaunchSource,
                SuccessPolicy = LauncherFlowCoordinator.ResolveSuccessPolicyKey(context)
            };

            var response = await new LauncherCoordinatorIpcClient()
                .SendAsync(activeCoordinatorAttempt.CoordinatorPipeName, request, TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);

            if (response is not null)
            {
                reporter?.Report("activation", response.Message);
                await DismissSplashIfNeededAsync(splashWindow).ConfigureAwait(false);
                return new LauncherResult
                {
                    Success = response.Accepted,
                    Stage = "launch",
                    Code = response.Code,
                    Message = response.Message,
                    Details = BuildCoordinatorResultDetails(response.Status, response.ActivationResult)
                };
            }
        }

        var activation = await TryActivateExistingInstanceWithStatusAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        if (activation is not null)
        {
            reporter?.Report("activation", activation.Message);
            await DismissSplashIfNeededAsync(splashWindow).ConfigureAwait(false);
            return new LauncherResult
            {
                Success = activation.Accepted,
                Stage = "launch",
                Code = activation.Accepted ? "existing_host_activated" : "existing_host_activation_failed",
                Message = activation.Message,
                Details = BuildCoordinatorResultDetails(null, activation)
            };
        }

        await DismissSplashIfNeededAsync(splashWindow).ConfigureAwait(false);
        return new LauncherResult
        {
            Success = false,
            Stage = "launch",
            Code = "launcher_coordinator_unavailable",
            Message = "Another Launcher is coordinating startup, but it did not respond in time.",
            Details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["activeCoordinatorPid"] = activeCoordinatorAttempt?.CoordinatorPid.ToString() ?? string.Empty,
                ["activeCoordinatorPipeName"] = activeCoordinatorAttempt?.CoordinatorPipeName ?? string.Empty,
                ["activeAttemptId"] = activeCoordinatorAttempt?.AttemptId ?? string.Empty,
                ["activeHostPid"] = activeCoordinatorAttempt?.HostPid.ToString() ?? string.Empty
            }
        };
    }

    private static async Task<LauncherCoordinatorResponse> HandleCoordinatorRequestAsync(
        LauncherCoordinatorRequest request,
        LauncherCoordinatorStatus status)
    {
        if (string.Equals(request.Command, LauncherCoordinatorCommands.ActivateDesktop, StringComparison.OrdinalIgnoreCase))
        {
            var activation = await TryActivateExistingInstanceWithStatusAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            if (activation is not null)
            {
                return new LauncherCoordinatorResponse
                {
                    Accepted = activation.Accepted,
                    Code = activation.Accepted ? "existing_host_activated" : "existing_host_activation_failed",
                    Message = activation.Message,
                    Status = status,
                    ActivationResult = activation
                };
            }

            return new LauncherCoordinatorResponse
            {
                Accepted = true,
                Code = "attached_to_launcher_coordinator",
                Message = "Attached to the active Launcher coordinator; desktop startup is still in progress.",
                Status = status
            };
        }

        return new LauncherCoordinatorResponse
        {
            Accepted = true,
            Code = "attached_to_launcher_coordinator",
            Message = "Attached to the active Launcher coordinator.",
            Status = status
        };
    }

    private static LauncherCoordinatorStatus BuildCoordinatorStatusFromAttempt(StartupAttemptRecord attempt)
    {
        return new LauncherCoordinatorStatus
        {
            AttemptId = attempt.AttemptId,
            CoordinatorPid = Environment.ProcessId,
            HostPid = attempt.HostPid,
            HostProcessAlive = TryGetLiveProcess(attempt.HostPid),
            LaunchSource = attempt.LaunchSource,
            SuccessPolicy = attempt.SuccessPolicy,
            LastObservedStage = attempt.LastObservedStage,
            LastObservedMessage = attempt.LastObservedMessage,
            PublicIpcConnected = attempt.PublicIpcConnected || attempt.IpcConnected,
            State = attempt.State.ToString(),
            SoftTimeoutShown = attempt.State is StartupAttemptState.SoftTimeout or StartupAttemptState.DetachedWaiting,
            Completed = attempt.State is StartupAttemptState.Succeeded or StartupAttemptState.Failed,
            Succeeded = attempt.State == StartupAttemptState.Succeeded,
            UpdatedAtUtc = attempt.UpdatedAtUtc
        };
    }

    private static Dictionary<string, string> BuildCoordinatorResultDetails(
        LauncherCoordinatorStatus? status,
        PublicShellActivationResult? activation)
    {
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["coordinatorPid"] = status?.CoordinatorPid.ToString() ?? string.Empty,
            ["coordinatorAttemptId"] = status?.AttemptId ?? string.Empty,
            ["hostPid"] = status?.HostPid.ToString() ?? activation?.Status.ProcessId.ToString() ?? string.Empty,
            ["hostProcessAlive"] = status?.HostProcessAlive.ToString() ?? string.Empty,
            ["publicIpcConnected"] = (status?.PublicIpcConnected ?? activation is not null).ToString(),
            ["startupStage"] = status?.LastObservedStage.ToString() ?? string.Empty,
            ["startupState"] = status?.State ?? string.Empty,
            ["activationAccepted"] = activation?.Accepted.ToString() ?? string.Empty,
            ["shellState"] = activation?.Status.ShellState ?? status?.ShellStatus?.ShellState ?? string.Empty,
            ["trayState"] = activation?.Status.Tray.State ?? status?.ShellStatus?.Tray.State ?? string.Empty,
            ["taskbarUsable"] = activation?.Status.Taskbar.IsUsable.ToString() ?? status?.ShellStatus?.Taskbar.IsUsable.ToString() ?? string.Empty
        };

        return details;
    }

    private static async Task DismissSplashIfNeededAsync(SplashWindow? splashWindow)
    {
        if (splashWindow is null)
        {
            return;
        }

        try
        {
            await splashWindow.DismissAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to dismiss splash after coordinator attach: {ex.Message}");
        }
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

    private static async Task<ErrorWindowResult> ShowFailureWindowAsync(LauncherResult result)
    {
        ErrorWindow? errorWindow = null;
        var hostProcessAlive = result.Details.TryGetValue("hostProcessAlive", out var hostProcessAliveText) &&
                               bool.TryParse(hostProcessAliveText, out var hostProcessAliveValue) &&
                               hostProcessAliveValue;
        var hostPid = result.Details.TryGetValue("hostPid", out var hostPidText) &&
                      int.TryParse(hostPidText, out var parsedPid)
            ? parsedPid
            : (int?)null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                errorWindow = new ErrorWindow();
                if (hostProcessAlive)
                {
                    errorWindow.ConfigureForRunningHostFailure(hostPid);
                }
                else
                {
                    errorWindow.ConfigureForGenericFailure(allowRetry: true);
                }

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
            return ErrorWindowResult.Exit;
        }

        try
        {
            return await errorWindow.WaitForChoiceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("Failure window closed unexpectedly.", ex);
            return ErrorWindowResult.Exit;
        }
    }

    private static async Task<bool> TryActivateExistingInstanceAsync()
    {
        var activation = await TryActivateExistingInstanceWithStatusAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        return activation?.Accepted == true;
    }

    private static async Task<PublicShellActivationResult?> TryActivateExistingInstanceWithStatusAsync(TimeSpan timeout)
    {
        try
        {
            using var ipcClient = new LanMountainDesktopIpcClient();
            var connectTask = ipcClient.ConnectAsync();
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(timeout)).ConfigureAwait(false);
            if (completedTask != connectTask)
            {
                return null;
            }

            await connectTask.ConfigureAwait(false);
            if (!ipcClient.IsConnected)
            {
                return null;
            }

            var shellProxy = ipcClient.CreateProxy<IPublicShellControlService>();
            var activationTask = shellProxy.ActivateMainWindowWithStatusAsync();
            completedTask = await Task.WhenAny(activationTask, Task.Delay(timeout)).ConfigureAwait(false);
            if (completedTask != activationTask)
            {
                return null;
            }

            return await activationTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to activate the existing desktop instance: {ex.Message}");
            return null;
        }
    }

    private static bool TryGetLiveProcess(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
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
