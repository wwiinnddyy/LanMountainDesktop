using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Startup;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Shell;

internal sealed class LauncherOrchestrator
{
    private readonly CommandContext _context;
    private readonly DeploymentLocator _deploymentLocator;
    private readonly OobeStateService _oobeStateService;
    private readonly StartupAttemptRegistry _startupAttemptRegistry;
    private readonly LauncherCoordinatorIpcServer? _coordinatorIpcServer;
    private readonly DataLocationResolver _dataLocationResolver;
    private readonly IReadOnlyList<IOobeStep> _oobeSteps;
    private readonly LaunchPipeline _pipeline;

    public LauncherOrchestrator(
        CommandContext context,
        DeploymentLocator deploymentLocator,
        OobeStateService oobeStateService,
        StartupAttemptRegistry startupAttemptRegistry,
        LauncherCoordinatorIpcServer? coordinatorIpcServer = null,
        LaunchPipeline? pipeline = null)
    {
        _context = context;
        _deploymentLocator = deploymentLocator;
        _oobeStateService = oobeStateService;
        _startupAttemptRegistry = startupAttemptRegistry;
        _coordinatorIpcServer = coordinatorIpcServer;
        _dataLocationResolver = new DataLocationResolver(deploymentLocator.GetAppRoot());
        _oobeSteps =
        [
            new WelcomeOobeStep(_oobeStateService, _context),
            new DataLocationOobeStep(_dataLocationResolver)
        ];
        _pipeline = pipeline ?? new LaunchPipeline(
        [
            new CleanupDeploymentsPhase(),
            new ExistingHostProbePhase(),
            new OobeGatePhase(),
            new LaunchHostPhase(),
            new MonitorStartupPhase()
        ]);
    }

    public static string ResolveSuccessPolicyKey(CommandContext context) =>
        new StartupSuccessTracker(context).PolicyKey;

    public async Task<LauncherResult> RunAsync(SplashWindow? existingSplashWindow = null)
    {
        try
        {
            var oobeDecision = _oobeStateService.Evaluate(_context);
            if (oobeDecision.ShouldShowOobe)
            {
                var legacyInfo = LegacyVersionDetector.DetectLegacyInstallation();
                if (legacyInfo is not null)
                {
                    var migrationResult = await LaunchUiPresenter.ShowMigrationPromptAsync(legacyInfo).ConfigureAwait(false);
                    Logger.Info($"Migration prompt completed. Result='{migrationResult}'.");
                }
            }

            var splashWindow = existingSplashWindow ?? await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new SplashWindow();
                window.Show();
                return window;
            });
            var versionInfo = _deploymentLocator.GetVersionInfo();
            splashWindow.SetVersionInfo(versionInfo.Version, versionInfo.Codename);
            var reporter = (ISplashStageReporter)splashWindow;

            LoadingDetailsWindow? loadingDetailsWindow = null;
            if (_context.IsDebugMode || _context.GetOption("show-loading-details") == "true")
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    loadingDetailsWindow = new LoadingDetailsWindow();
                    loadingDetailsWindow.Show();
                });
            }

            var successTcs = new TaskCompletionSource<StartupSuccessState>(TaskCreationOptions.RunContinuationsAsynchronously);
            var activationFailedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lastStage = StartupStage.Initializing;
            var lastStageMessage = "launcher-started";
            var startupSuccessTracker = new StartupSuccessTracker(_context);
            var activationFailureReason = string.Empty;
            var ipcConnected = false;
            var softTimeoutShown = false;
            var attachedToExistingAttempt = false;
            var windowsClosingByOrchestrator = false;
            StartupAttemptRecord? trackedAttempt = null;
            PublicShellStatus? shellStatus = null;
            var loadingState = new LoadingStateMessage();

            void PublishCoordinatorStatus(bool? hostProcessAliveOverride = null, bool completed = false, bool succeeded = false)
            {
                if (_coordinatorIpcServer is null)
                {
                    return;
                }

                trackedAttempt = _startupAttemptRegistry.GetOwnedAttempt() ?? trackedAttempt;
                var hostPid = trackedAttempt?.HostPid ?? 0;
                var hostProcessAlive = hostProcessAliveOverride ??
                                       (hostPid > 0 && LaunchResultBuilder.TryGetLiveProcess(hostPid, out _));
                var status = new LauncherCoordinatorStatus
                {
                    AttemptId = trackedAttempt?.AttemptId ?? string.Empty,
                    CoordinatorPid = Environment.ProcessId,
                    HostPid = hostPid,
                    HostProcessAlive = hostProcessAlive,
                    LaunchSource = trackedAttempt?.LaunchSource ?? _context.LaunchSource,
                    SuccessPolicy = trackedAttempt?.SuccessPolicy ?? startupSuccessTracker.PolicyKey,
                    LastObservedStage = lastStage,
                    LastObservedMessage = lastStageMessage,
                    PublicIpcConnected = ipcConnected,
                    State = trackedAttempt?.State.ToString() ?? StartupAttemptState.Pending.ToString(),
                    SoftTimeoutShown = softTimeoutShown,
                    Completed = completed,
                    Succeeded = succeeded,
                    ShellStatus = shellStatus,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };

                _coordinatorIpcServer.UpdateStatus(status);
                _startupAttemptRegistry.UpdateOwnedCoordinatorHeartbeat(status);
            }

            trackedAttempt = _startupAttemptRegistry.GetOwnedAttempt();
            PublishCoordinatorStatus();

            EventHandler? splashClosedHandler = null;
            splashClosedHandler = (_, _) =>
            {
                if (windowsClosingByOrchestrator)
                {
                    return;
                }

                _startupAttemptRegistry.MarkOwnedDetachedWaiting();
                Logger.Warn("Splash window was closed manually. Launcher will continue monitoring the current startup attempt.");
            };
            splashWindow.Closed += splashClosedHandler;

            using var ipcClient = new LanMountainDesktopIpcClient();
            ipcClient.RegisterNotifyHandler<StartupProgressMessage>(IpcRoutedNotifyIds.LauncherStartupProgress, message =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        ipcConnected = true;
                        lastStage = message.Stage;
                        lastStageMessage = message.Message ?? message.Stage.ToString();
                        Logger.Info($"IPC stage received. Stage='{message.Stage}'; Message='{message.Message ?? string.Empty}'.");

                        loadingState = loadingState with
                        {
                            Stage = message.Stage,
                            OverallProgressPercent = message.ProgressPercent,
                            Message = message.Message,
                            Timestamp = DateTimeOffset.UtcNow
                        };

                        reporter.Report(LaunchUiPresenter.MapStartupStageToSplashStage(message.Stage), message.Message ?? message.Stage.ToString());
                        loadingDetailsWindow?.UpdateLoadingState(loadingState);
                        _startupAttemptRegistry.UpdateOwnedStage(message.Stage, message.Message, ipcConnected: true);
                        PublishCoordinatorStatus();

                        if (startupSuccessTracker.TryResolve(message.Stage, out var successState))
                        {
                            successTcs.TrySetResult(successState);
                        }

                        if (message.Stage == StartupStage.ActivationFailed)
                        {
                            activationFailureReason = message.Message ?? "activation_failed";
                            activationFailedTcs.TrySetResult(message.Message ?? "activation_failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("IPC progress callback failed.", ex);
                    }
                });
            });
            ipcClient.RegisterNotifyHandler<LoadingStateMessage>(IpcRoutedNotifyIds.LauncherLoadingState, message =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        loadingState = message;
                        loadingDetailsWindow?.UpdateLoadingState(loadingState);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("IPC loading-state callback failed.", ex);
                    }
                });
            });

            var launchContext = new LaunchContext
            {
                CommandContext = _context,
                DeploymentLocator = _deploymentLocator,
                OobeStateService = _oobeStateService,
                StartupAttemptRegistry = _startupAttemptRegistry,
                CoordinatorIpcServer = _coordinatorIpcServer,
                DataLocationResolver = _dataLocationResolver,
                OobeSteps = _oobeSteps,
                SplashWindow = splashWindow,
                LoadingDetailsWindow = loadingDetailsWindow,
                Reporter = reporter,
                IpcClient = ipcClient,
                SuccessTracker = startupSuccessTracker,
                SuccessTcs = successTcs,
                ActivationFailedTcs = activationFailedTcs,
                LoadingState = loadingState,
                PublishCoordinatorStatus = PublishCoordinatorStatus,
                SplashClosedHandler = splashClosedHandler
            };

            try
            {
                var result = await _pipeline.ExecuteAsync(launchContext).ConfigureAwait(false);
                windowsClosingByOrchestrator = launchContext.WindowsClosingByOrchestrator;
                return result;
            }
            finally
            {
                if (splashClosedHandler is not null)
                {
                    splashWindow.Closed -= splashClosedHandler;
                }

                if (!windowsClosingByOrchestrator && !launchContext.WindowsClosingByOrchestrator)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try
                        {
                            if (splashWindow.IsVisible && splashWindow.IsLoaded)
                            {
                                splashWindow.Close();
                                Logger.Info("Splash window closed in orchestrator cleanup.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Failed to close splash window during orchestrator cleanup.", ex);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Launcher orchestrator failed.", ex);
            var oobeDecision = _oobeStateService.Evaluate(_context);
            return LaunchResultBuilder.Build(
                false,
                "launch",
                "exception",
                ex.Message,
                LaunchResultBuilder.BuildLauncherContextDetails(_context, oobeDecision, _deploymentLocator.GetAppRoot()),
                ex.ToString());
        }
    }
}
