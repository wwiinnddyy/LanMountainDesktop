using System.Diagnostics;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Resources;
using LanMountainDesktop.Launcher.Startup;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Services;

internal sealed partial class LauncherFlowCoordinator
{
    private static readonly string SoftTimeoutStatusMessage = Strings.Coordinator_SlowDeviceMessage;
    private static readonly string SoftTimeoutDetailsMessage = Strings.Coordinator_RunningHostMessage;

    private readonly CommandContext _context;
    private readonly DeploymentLocator _deploymentLocator;
    private readonly OobeStateService _oobeStateService;
    private readonly UpdateEngineService _updateEngine;
    private readonly StartupAttemptRegistry _startupAttemptRegistry;
    private readonly LauncherCoordinatorIpcServer? _coordinatorIpcServer;
    private readonly DataLocationResolver _dataLocationResolver;
    private readonly IReadOnlyList<IOobeStep> _oobeSteps;

    public LauncherFlowCoordinator(
        CommandContext context,
        DeploymentLocator deploymentLocator,
        OobeStateService oobeStateService,
        UpdateEngineService updateEngine,
        StartupAttemptRegistry? startupAttemptRegistry = null,
        LauncherCoordinatorIpcServer? coordinatorIpcServer = null)
    {
        _context = context;
        _deploymentLocator = deploymentLocator;
        _oobeStateService = oobeStateService;
        _updateEngine = updateEngine;
        _startupAttemptRegistry = startupAttemptRegistry ?? new StartupAttemptRegistry();
        _coordinatorIpcServer = coordinatorIpcServer;
        _dataLocationResolver = new DataLocationResolver(deploymentLocator.GetAppRoot());
        _oobeSteps =
        [
            new WelcomeOobeStep(_oobeStateService, _context),
            new DataLocationOobeStep(_dataLocationResolver)
        ];
    }

    public static string ResolveSuccessPolicyKey(CommandContext context)
    {
        return new StartupSuccessTracker(context).PolicyKey;
    }

    public async Task<LauncherResult> RunAsync(SplashWindow? existingSplashWindow = null)
    {
        try
        {
            _deploymentLocator.CleanupOldDeployments(minVersionsToKeep: 3);
            var oobeDecision = _oobeStateService.Evaluate(_context);
            var launcherContextDetails = BuildLauncherContextDetails(_context, oobeDecision, _deploymentLocator.GetAppRoot());

            if (oobeDecision.ShouldShowOobe)
            {
                var legacyInfo = LegacyVersionDetector.DetectLegacyInstallation();
                if (legacyInfo is not null)
                {
                    var migrationResult = await ShowMigrationPromptAsync(legacyInfo).ConfigureAwait(false);
                    Logger.Info($"Migration prompt completed. Result='{migrationResult}'.");
                }
            }

            var splashWindow = existingSplashWindow ?? await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new SplashWindow();
                window.Show();
                return window;
            });
            var windowsClosingByCoordinator = false;
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
            StartupAttemptRecord? trackedAttempt = null;
            PublicShellStatus? shellStatus = null;

            void PublishCoordinatorStatus(bool? hostProcessAliveOverride = null, bool completed = false, bool succeeded = false)
            {
                if (_coordinatorIpcServer is null)
                {
                    return;
                }

                trackedAttempt = _startupAttemptRegistry.GetOwnedAttempt() ?? trackedAttempt;
                var hostPid = trackedAttempt?.HostPid ?? 0;
                var hostProcessAlive = hostProcessAliveOverride ??
                                       (hostPid > 0 && TryGetLiveProcess(hostPid, out _));
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

            var loadingState = new LoadingStateMessage();
            EventHandler? splashClosedHandler = null;
            splashClosedHandler = (_, _) =>
            {
                if (windowsClosingByCoordinator)
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

                        reporter.Report(MapStartupStageToSplashStage(message.Stage), message.Message ?? message.Stage.ToString());
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

            try
            {
                if (HostActivationPolicy.ShouldProbeExistingHostBeforeLaunch(_context))
                {
                    var multiInstanceBehavior = LoadMultiInstanceLaunchBehavior();
                    var existingShellStatus = await TryGetExistingHostStatusAsync(ipcClient, StartupTimeoutPolicy.ExistingHostProbeTimeout)
                        .ConfigureAwait(false);
                    if (HostActivationPolicy.IsExistingHostReadyForLauncherDecision(existingShellStatus))
                    {
                        ipcConnected = true;
                        shellStatus = existingShellStatus;
                        var decisionResult = await ApplyExistingHostBehaviorAsync(
                                ipcClient,
                                multiInstanceBehavior,
                                existingShellStatus!)
                            .ConfigureAwait(false);
                        shellStatus = decisionResult.ActivationResult?.Status ?? existingShellStatus;
                        var recoverableActivationFailure = decisionResult.ActivationResult is not null &&
                                                           HostActivationPolicy.IsRecoverableActivationFailure(decisionResult.ActivationResult);
                        lastStage = decisionResult.Success || recoverableActivationFailure
                            ? StartupStage.ActivationRedirected
                            : StartupStage.ActivationFailed;
                        lastStageMessage = decisionResult.Message;
                        if (decisionResult.Success || recoverableActivationFailure)
                        {
                            _startupAttemptRegistry.MarkOwnedSucceeded(lastStage, lastStageMessage);
                        }
                        else
                        {
                            _startupAttemptRegistry.MarkOwnedFailed(lastStage, lastStageMessage);
                        }

                        PublishCoordinatorStatus(hostProcessAliveOverride: true, completed: true, succeeded: decisionResult.Success);
                        windowsClosingByCoordinator = true;
                        await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
                        return BuildResult(
                            success: decisionResult.Success,
                            stage: "launch",
                            code: decisionResult.Code,
                            message: decisionResult.Message,
                            details: MergeDetails(
                                launcherContextDetails,
                                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["publicIpcConnected"] = "true",
                                    ["multiInstanceBehavior"] = multiInstanceBehavior.ToString(),
                                    ["existingHostPid"] = shellStatus?.ProcessId.ToString() ?? string.Empty,
                                    ["existingShellState"] = shellStatus?.ShellState ?? string.Empty,
                                    ["existingTrayState"] = shellStatus?.Tray.State ?? string.Empty,
                                    ["existingTaskbarUsable"] = shellStatus?.Taskbar.IsUsable.ToString() ?? string.Empty,
                                    ["activationAccepted"] = decisionResult.ActivationResult?.Accepted.ToString() ?? string.Empty
                                }));
                    }
                }

                reporter.Report("update", "Checking updates...");
                var updateResult = await _updateEngine.ApplyPendingUpdateAsync().ConfigureAwait(false);
                if (!updateResult.Success)
                {
                    Logger.Warn($"Update apply failed, will try to launch existing version. Error='{updateResult.Message}'.");
                    reporter.Report("update", "Update failed, launching existing version...");
                    // Clean up corrupted update files to prevent repeated failures
                    try
                    {
                        _updateEngine.CleanupIncomingArtifacts();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to cleanup update artifacts after failed update: {ex.Message}");
                    }
                    // Continue to launch existing version instead of aborting
                }

                if (oobeDecision.ShouldShowOobe)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => splashWindow.Hide());
                    foreach (var step in _oobeSteps)
                    {
                        await step.RunAsync(CancellationToken.None).ConfigureAwait(false);
                    }

                    await Dispatcher.UIThread.InvokeAsync(() => splashWindow.Show());
                }

                reporter.Report("launch", "Launching desktop...");
                var launchOutcome = default(HostLaunchOutcome);
                var attachableAttempt = _startupAttemptRegistry.TryGetAttachableAttempt(_context.LaunchSource, startupSuccessTracker.PolicyKey);
                if (attachableAttempt is not null &&
                    _startupAttemptRegistry.AdoptAttempt(attachableAttempt.AttemptId) &&
                    TryGetLiveProcess(attachableAttempt.HostPid, out var attachedProcess))
                {
                    trackedAttempt = attachableAttempt;
                    attachedToExistingAttempt = true;
                    ipcConnected = attachableAttempt.IpcConnected;
                    lastStage = attachableAttempt.LastObservedStage;
                    lastStageMessage = string.IsNullOrWhiteSpace(attachableAttempt.LastObservedMessage)
                        ? "Attached to the existing startup attempt."
                        : attachableAttempt.LastObservedMessage;
                    reporter.Report(MapStartupStageToSplashStage(lastStage), lastStageMessage);
                    PublishCoordinatorStatus(hostProcessAliveOverride: true);

                    if (startupSuccessTracker.TryResolve(lastStage, out var attachedSuccessState))
                    {
                        windowsClosingByCoordinator = true;
                        _startupAttemptRegistry.MarkOwnedSucceeded(attachedSuccessState.Stage, attachedSuccessState.Message);
                        await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
                        return BuildResult(
                            success: true,
                            stage: "launch",
                            code: attachedSuccessState.Code,
                            message: attachedSuccessState.Message,
                            details: MergeDetails(
                                launcherContextDetails,
                                BuildAttemptDetails(
                                    trackedAttempt,
                                    attachedToExistingAttempt,
                                    ipcConnected,
                                    hostProcessAlive: true,
                                    lastStage,
                                    lastStageMessage,
                                    activationFailureReason,
                                    softTimeoutShown: false,
                                    recoveryActivationAttempted: false)));
                    }

                    if (attachableAttempt.State is StartupAttemptState.SoftTimeout or StartupAttemptState.DetachedWaiting)
                    {
                        softTimeoutShown = true;
                        reporter.Report("delayed", SoftTimeoutStatusMessage);
                        loadingState = HostStartupMonitor.BuildDelayedLoadingState(
                            loadingState,
                            SoftTimeoutStatusMessage,
                            SoftTimeoutDetailsMessage,
                            trackedAttempt.StartedAtUtc);
                        loadingDetailsWindow?.UpdateLoadingState(loadingState);
                    }

                    launchOutcome = HostLaunchOutcome.FromProcess(
                        attachedProcess!,
                        BuildResult(
                            true,
                            "launchHost",
                            "attached_attempt",
                            "Attached to an existing startup attempt.",
                            BuildAttemptDetails(
                                trackedAttempt,
                                attachedToExistingAttempt,
                                ipcConnected,
                                hostProcessAlive: true,
                                lastStage,
                                lastStageMessage,
                                activationFailureReason,
                                softTimeoutShown,
                                recoveryActivationAttempted: false)),
                        BuildAttemptDetails(
                            trackedAttempt,
                            attachedToExistingAttempt,
                            ipcConnected,
                            hostProcessAlive: true,
                            lastStage,
                            lastStageMessage,
                            activationFailureReason,
                            softTimeoutShown,
                            recoveryActivationAttempted: false));
                }
                else
                {
                    launchOutcome = await LaunchHostWithIpcAsync().ConfigureAwait(false);
                }

                if (!launchOutcome.Result.Success)
                {
                    return WithAdditionalDetails(launchOutcome.Result, launcherContextDetails);
                }

                if (launchOutcome.ImmediateResult is not null)
                {
                    await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
                    return WithAdditionalDetails(launchOutcome.ImmediateResult, launcherContextDetails);
                }

                if (launchOutcome.Process is null)
                {
                    return BuildResult(
                        success: false,
                        stage: "launch",
                        code: "host_start_failed",
                        message: "Host launch did not create a process.",
                        details: MergeDetails(
                            launcherContextDetails,
                            MergeDetails(
                                launchOutcome.Details,
                                BuildAttemptDetails(
                                    trackedAttempt,
                                    attachedToExistingAttempt,
                                    ipcConnected,
                                    hostProcessAlive: false,
                                    lastStage,
                                    lastStageMessage,
                                    activationFailureReason,
                                    softTimeoutShown,
                                    recoveryActivationAttempted: false))));
                }

                if (!attachedToExistingAttempt)
                {
                    var reservedAttempt = _startupAttemptRegistry.GetOwnedAttempt();
                    trackedAttempt = reservedAttempt is { ReservedBeforeHostStart: true }
                        ? _startupAttemptRegistry.AssignOwnedHostProcess(
                            launchOutcome.Process.Id,
                            lastStage,
                            lastStageMessage)
                        : _startupAttemptRegistry.StartOwnedAttempt(
                            launchOutcome.Process.Id,
                            _context.LaunchSource,
                            startupSuccessTracker.PolicyKey,
                            lastStage,
                            lastStageMessage);
                    PublishCoordinatorStatus(hostProcessAliveOverride: true);
                }

                Dictionary<string, string> ComposeLaunchDetails(bool hostProcessAlive, bool recoveryActivationAttempted = false)
                {
                    return MergeDetails(
                        launcherContextDetails,
                        MergeDetails(
                            launchOutcome.Details,
                            BuildAttemptDetails(
                                trackedAttempt,
                                attachedToExistingAttempt,
                                ipcConnected,
                                hostProcessAlive,
                                lastStage,
                                lastStageMessage,
                                activationFailureReason,
                                softTimeoutShown,
                                recoveryActivationAttempted)));
                }

                var monitor = new HostStartupMonitor();
                var monitorOutcome = await monitor.MonitorUntilCompleteAsync(new HostStartupMonitor.Request(
                    launchOutcome.Process,
                    ipcClient,
                    startupSuccessTracker,
                    _startupAttemptRegistry,
                    trackedAttempt,
                    attachedToExistingAttempt,
                    launcherContextDetails,
                    successTcs,
                    activationFailedTcs,
                    reporter,
                    loadingDetailsWindow,
                    loadingState,
                    lastStage,
                    lastStageMessage,
                    ipcConnected,
                    activationFailureReason,
                    softTimeoutShown,
                    (hostProcessAliveOverride, completed, succeeded) =>
                        PublishCoordinatorStatus(hostProcessAliveOverride, completed, succeeded),
                    ComposeLaunchDetails)).ConfigureAwait(false);

                windowsClosingByCoordinator = true;
                await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
                return BuildResult(
                    success: monitorOutcome.Success,
                    stage: "launch",
                    code: monitorOutcome.Code,
                    message: monitorOutcome.Message,
                    details: monitorOutcome.Details);
            }
            finally
            {
                if (splashClosedHandler is not null)
                {
                    splashWindow.Closed -= splashClosedHandler;
                }

                if (!windowsClosingByCoordinator)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try
                        {
                            if (splashWindow.IsVisible && splashWindow.IsLoaded)
                            {
                                splashWindow.Close();
                                Logger.Info("Splash window closed in coordinator cleanup.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Failed to close splash window during coordinator cleanup.", ex);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Launcher coordinator failed.", ex);
            return BuildResult(
                success: false,
                stage: "launch",
                code: "exception",
                message: ex.Message,
                details: BuildLauncherContextDetails(_context, _oobeStateService.Evaluate(_context), _deploymentLocator.GetAppRoot()),
                errorMessage: ex.ToString());
        }
    }





    private static LauncherResult BuildResult(
        bool success,
        string stage,
        string code,
        string message,
        Dictionary<string, string>? details = null,
        string? errorMessage = null)
    {
        Logger.Info($"Launcher result prepared. Success={success}; Stage='{stage}'; Code='{code}'.");
        return new LauncherResult
        {
            Success = success,
            Stage = stage,
            Code = code,
            Message = message,
            ErrorMessage = errorMessage,
            Details = details ?? []
        };
    }

    private static LauncherResult WithAdditionalDetails(LauncherResult result, Dictionary<string, string> details)
    {
        return new LauncherResult
        {
            Success = result.Success,
            Stage = result.Stage,
            Code = result.Code,
            Message = result.Message,
            CurrentVersion = result.CurrentVersion,
            TargetVersion = result.TargetVersion,
            RolledBackTo = result.RolledBackTo,
            Details = MergeDetails(details, result.Details),
            InstalledPackagePath = result.InstalledPackagePath,
            ManifestId = result.ManifestId,
            ManifestName = result.ManifestName,
            ErrorMessage = result.ErrorMessage
        };
    }

    private static Dictionary<string, string> BuildLauncherContextDetails(
        CommandContext context,
        OobeLaunchDecision oobeDecision,
        string appRoot)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["command"] = context.Command,
            ["launchSource"] = context.LaunchSource,
            ["isGuiMode"] = context.IsGuiCommand.ToString(),
            ["isDebugMode"] = context.IsDebugMode.ToString(),
            ["isElevated"] = oobeDecision.IsElevated.ToString(),
            ["resolvedAppRoot"] = appRoot,
            ["oobeStatePath"] = oobeDecision.StatePath,
            ["oobeStateStatus"] = oobeDecision.Status.ToString(),
            ["oobeDecision"] = oobeDecision.ShouldShowOobe ? "show" : "skip",
            ["oobeSuppressionReason"] = oobeDecision.SuppressionReason,
            ["oobeResultCode"] = oobeDecision.ResultCode,
            ["userSid"] = oobeDecision.UserSid ?? string.Empty,
            ["usedLegacyOobeMarker"] = oobeDecision.UsedLegacyMarker.ToString(),
            ["migratedLegacyOobeMarker"] = oobeDecision.MigratedLegacyMarker.ToString(),
            ["oobeStateError"] = oobeDecision.ErrorMessage
        };
    }


    private static Dictionary<string, string> MergeDetails(
        Dictionary<string, string> left,
        Dictionary<string, string> right)
    {
        var merged = new Dictionary<string, string>(left, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in right)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }
}

