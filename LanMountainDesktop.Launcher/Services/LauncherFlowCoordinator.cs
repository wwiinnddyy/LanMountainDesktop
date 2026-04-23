using System.Diagnostics;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class LauncherFlowCoordinator
{
    private static readonly TimeSpan StartupSoftTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupHardTimeout = TimeSpan.FromSeconds(120);
    private const string SoftTimeoutStatusMessage = "设备较慢，仍在启动，请稍候。";
    private const string SoftTimeoutDetailsMessage = "桌面主进程仍在运行，Launcher 会继续等待，不会重复启动。";

    private static readonly string[] LauncherOnlyOptions =
    [
        "debug", "show-loading-details", "plugins-dir", "source", "result",
        "app-root",
        LauncherIpcConstants.LauncherPidEnvVar,
        LauncherIpcConstants.PackageRootEnvVar,
        LauncherIpcConstants.VersionEnvVar,
        LauncherIpcConstants.CodenameEnvVar
    ];

    private readonly CommandContext _context;
    private readonly DeploymentLocator _deploymentLocator;
    private readonly OobeStateService _oobeStateService;
    private readonly UpdateEngineService _updateEngine;
    private readonly PluginInstallerService _pluginInstallerService;
    private readonly StartupAttemptRegistry _startupAttemptRegistry;
    private readonly IReadOnlyList<IOobeStep> _oobeSteps;

    public LauncherFlowCoordinator(
        CommandContext context,
        DeploymentLocator deploymentLocator,
        OobeStateService oobeStateService,
        UpdateEngineService updateEngine,
        PluginInstallerService pluginInstallerService)
    {
        _context = context;
        _deploymentLocator = deploymentLocator;
        _oobeStateService = oobeStateService;
        _updateEngine = updateEngine;
        _pluginInstallerService = pluginInstallerService;
        _startupAttemptRegistry = new StartupAttemptRegistry();
        _oobeSteps = [new WelcomeOobeStep(_oobeStateService, _context)];
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
                reporter.Report("update", "Checking updates...");
                var updateResult = await _updateEngine.ApplyPendingUpdateAsync().ConfigureAwait(false);
                if (!updateResult.Success)
                {
                    return WithAdditionalDetails(updateResult, launcherContextDetails);
                }

                reporter.Report("plugins", "Applying plugin upgrades...");
                var pluginsDir = _context.GetOption("plugins-dir") ?? Path.Combine(_deploymentLocator.GetAppRoot(), "plugins");
                var queueResult = new PluginUpgradeQueueService(_pluginInstallerService).ApplyPendingUpgrades(pluginsDir);
                if (!queueResult.Success)
                {
                    return WithAdditionalDetails(queueResult, launcherContextDetails);
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
                        loadingState = BuildDelayedLoadingState(
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
                    trackedAttempt = _startupAttemptRegistry.StartOwnedAttempt(
                        launchOutcome.Process.Id,
                        _context.LaunchSource,
                        startupSuccessTracker.PolicyKey,
                        lastStage,
                        lastStageMessage);
                }

                var connected = await TryConnectToPublicIpcAsync(ipcClient, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (!connected)
                {
                    Logger.Warn("Timed out waiting for host public IPC. Launcher will continue without live startup notifications.");
                }
                else
                {
                    ipcConnected = true;
                    _startupAttemptRegistry.MarkOwnedIpcConnected();
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

                var processExitTask = launchOutcome.Process.WaitForExitAsync();
                var startedAt = trackedAttempt?.StartedAtUtc ?? DateTimeOffset.UtcNow;
                var softTimeoutAt = startedAt + StartupSoftTimeout;
                var hardTimeoutAt = startedAt + StartupHardTimeout;
                var nextReconnectAttemptAt = DateTimeOffset.UtcNow.AddSeconds(5);

                while (true)
                {
                    if (successTcs.Task.IsCompleted)
                    {
                        var successState = await successTcs.Task.ConfigureAwait(false);
                        windowsClosingByCoordinator = true;
                        _startupAttemptRegistry.MarkOwnedSucceeded(successState.Stage, successState.Message);
                        await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
                        return BuildResult(
                            success: true,
                            stage: "launch",
                            code: successState.Code,
                            message: successState.Message,
                            details: ComposeLaunchDetails(!launchOutcome.Process.HasExited));
                    }

                    if (activationFailedTcs.Task.IsCompleted && string.IsNullOrWhiteSpace(activationFailureReason))
                    {
                        activationFailureReason = await activationFailedTcs.Task.ConfigureAwait(false);
                        Logger.Warn($"Activation failure received before startup success. Reason='{activationFailureReason}'.");
                    }

                    if (processExitTask.IsCompleted)
                    {
                        var exitCode = launchOutcome.Process.ExitCode;
                        Logger.Warn($"Host exited before startup success criteria were met. ExitCode={exitCode}.");

                        windowsClosingByCoordinator = true;
                        if (exitCode == HostExitCodes.SecondaryActivationSucceeded)
                        {
                            _startupAttemptRegistry.MarkOwnedSucceeded(StartupStage.ActivationRedirected, "Host redirected activation to the existing desktop instance.");
                            await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
                            return BuildResult(
                                success: true,
                                stage: "launch",
                                code: "activation_redirected",
                                message: "Host redirected activation to the existing desktop instance.",
                                details: MergeDetails(
                                    ComposeLaunchDetails(hostProcessAlive: false),
                                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                    {
                                        ["exitCode"] = exitCode.ToString()
                                    }));
                        }

                        _startupAttemptRegistry.MarkOwnedFailed(lastStage, activationFailureReason);
                        await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
                        return BuildResult(
                            success: false,
                            stage: "launch",
                            code: exitCode is HostExitCodes.SecondaryActivationFailed or HostExitCodes.RestartLockNotAcquired
                                ? "activation_failed"
                                : "host_exited_early",
                            message: exitCode is HostExitCodes.SecondaryActivationFailed or HostExitCodes.RestartLockNotAcquired
                                ? $"Host activation handshake failed before the required startup state was reported. ExitCode={exitCode}."
                                : $"Host exited before the required startup state was reported. ExitCode={exitCode}.",
                            details: MergeDetails(
                                ComposeLaunchDetails(hostProcessAlive: false),
                                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["exitCode"] = exitCode.ToString()
                                }));
                    }

                    var now = DateTimeOffset.UtcNow;
                    if (!ipcConnected &&
                        !launchOutcome.Process.HasExited &&
                        now >= nextReconnectAttemptAt)
                    {
                        connected = await TryConnectToPublicIpcAsync(ipcClient, TimeSpan.FromMilliseconds(800)).ConfigureAwait(false);
                        if (connected)
                        {
                            ipcConnected = true;
                            _startupAttemptRegistry.MarkOwnedIpcConnected();
                        }

                        nextReconnectAttemptAt = DateTimeOffset.UtcNow.AddSeconds(5);
                    }

                    if (!softTimeoutShown &&
                        now >= softTimeoutAt &&
                        (!launchOutcome.Process.HasExited || ipcConnected))
                    {
                        softTimeoutShown = true;
                        _startupAttemptRegistry.MarkOwnedSoftTimeout(SoftTimeoutStatusMessage);
                        reporter.Report("delayed", SoftTimeoutStatusMessage);
                        loadingState = BuildDelayedLoadingState(
                            loadingState,
                            SoftTimeoutStatusMessage,
                            SoftTimeoutDetailsMessage,
                            trackedAttempt?.StartedAtUtc ?? startedAt);
                        loadingDetailsWindow?.UpdateLoadingState(loadingState);
                    }

                    if (now >= hardTimeoutAt)
                    {
                        break;
                    }

                    var nextCheckpointAt = hardTimeoutAt;
                    if (!softTimeoutShown && softTimeoutAt < nextCheckpointAt)
                    {
                        nextCheckpointAt = softTimeoutAt;
                    }

                    var delay = nextCheckpointAt - now;
                    if (delay > TimeSpan.FromSeconds(1))
                    {
                        delay = TimeSpan.FromSeconds(1);
                    }
                    else if (delay < TimeSpan.FromMilliseconds(100))
                    {
                        delay = TimeSpan.FromMilliseconds(100);
                    }

                    await Task.WhenAny(
                        successTcs.Task,
                        activationFailedTcs.Task,
                        processExitTask,
                        Task.Delay(delay)).ConfigureAwait(false);
                }

                var recoveryActivationAttempted = false;
                if (!connected && !launchOutcome.Process.HasExited)
                {
                    connected = await TryConnectToPublicIpcAsync(ipcClient, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    if (connected)
                    {
                        ipcConnected = true;
                        _startupAttemptRegistry.MarkOwnedIpcConnected();
                    }
                }

                if (connected && !launchOutcome.Process.HasExited)
                {
                    recoveryActivationAttempted = true;
                    var recoveryOutcome = await TryRecoverWithPublicActivationAsync(
                        ipcClient,
                        launchOutcome.Process,
                        successTcs.Task,
                        startupSuccessTracker).ConfigureAwait(false);
                    if (recoveryOutcome is not null)
                    {
                        windowsClosingByCoordinator = true;
                        _startupAttemptRegistry.MarkOwnedSucceeded(recoveryOutcome.Stage, recoveryOutcome.Message);
                        await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
                        return BuildResult(
                            success: true,
                            stage: "launch",
                            code: recoveryOutcome.Code,
                            message: recoveryOutcome.Message,
                            details: ComposeLaunchDetails(
                                !launchOutcome.Process.HasExited,
                                recoveryActivationAttempted: true));
                    }
                }

                windowsClosingByCoordinator = true;
                _startupAttemptRegistry.MarkOwnedFailed(lastStage, activationFailureReason);
                await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
                return BuildResult(
                    success: false,
                    stage: "launch",
                    code: "desktop_not_visible",
                    message: "Host process started, but it never reached the required startup state within 120 seconds.",
                    details: ComposeLaunchDetails(
                        !launchOutcome.Process.HasExited,
                        recoveryActivationAttempted));
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

    private async Task<LauncherResult?> RetryActivationAfterEarlyFailureAsync()
    {
        Logger.Warn("Attempting one explicit activation retry after host early failure.");
        var retryOutcome = await LaunchHostWithIpcAsync(forceDirectMode: true, retryTag: "explicit-activation-retry").ConfigureAwait(false);
        if (!retryOutcome.Result.Success)
        {
            return retryOutcome.Result;
        }

        if (retryOutcome.ImmediateResult is not null)
        {
            return retryOutcome.ImmediateResult;
        }

        if (retryOutcome.Process is not null)
        {
            var retryExitTask = retryOutcome.Process.WaitForExitAsync();
            var completed = await Task.WhenAny(retryExitTask, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false);

            if (completed != retryExitTask)
            {
                return BuildResult(
                    success: true,
                    stage: "launch",
                    code: "activation_retry_started",
                    message: "Activation retry started the host successfully.",
                    details: retryOutcome.Details);
            }

            if (retryOutcome.Process.ExitCode == HostExitCodes.SecondaryActivationSucceeded)
            {
                return BuildResult(
                    success: true,
                    stage: "launch",
                    code: "activation_redirected",
                    message: "Activation retry redirected to the existing desktop instance.",
                    details: retryOutcome.Details);
            }
        }

        return BuildResult(
            success: false,
            stage: "launch",
            code: "activation_failed",
            message: "Activation retry failed to make the desktop visible.",
            details: retryOutcome.Details);
    }

    private static async Task CloseWindowsAsync(SplashWindow splashWindow, LoadingDetailsWindow? loadingDetailsWindow)
    {
        try
        {
            await splashWindow.DismissAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to dismiss splash window.", ex);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (loadingDetailsWindow is not null && loadingDetailsWindow.IsVisible)
                {
                    loadingDetailsWindow.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to close loading details window.", ex);
            }
        });
    }

    private async Task<HostLaunchOutcome> LaunchHostWithIpcAsync(bool forceDirectMode = false, string? retryTag = null)
    {
        var resolution = _deploymentLocator.ResolveHostExecutable(_context);
        if (!resolution.Success || string.IsNullOrWhiteSpace(resolution.ResolvedHostPath))
        {
            var (errorResult, selectedPath) = await ShowHostNotFoundErrorAsync().ConfigureAwait(false);
            if (errorResult == ErrorWindowResult.Retry)
            {
                if (!string.IsNullOrWhiteSpace(selectedPath) && File.Exists(selectedPath))
                {
                    return await LaunchHostWithExplicitPathAsync(selectedPath, forceDirectMode, retryTag).ConfigureAwait(false);
                }

                return await LaunchHostWithIpcAsync(forceDirectMode, retryTag).ConfigureAwait(false);
            }

            return HostLaunchOutcome.FromResult(BuildResult(
                success: false,
                stage: "launchHost",
                code: "host_not_found",
                message: "LanMountainDesktop host executable was not found.",
                details: BuildResolutionDetails(resolution, null, null, "resolve")));
        }

        return await LaunchHostWithResolvedPathAsync(resolution, forceDirectMode, retryTag).ConfigureAwait(false);
    }

    private Task<HostLaunchOutcome> LaunchHostWithExplicitPathAsync(string hostPath, bool forceDirectMode, string? retryTag)
    {
        var resolution = new HostResolutionResult
        {
            Success = true,
            ResolvedHostPath = Path.GetFullPath(hostPath),
            ResolutionSource = "user_selected_path",
            AppRoot = _deploymentLocator.GetAppRoot(),
            ExplicitAppRoot = Path.GetDirectoryName(hostPath),
            SearchedPaths = [Path.GetFullPath(hostPath)]
        };

        return LaunchHostWithResolvedPathAsync(resolution, forceDirectMode, retryTag);
    }

    private async Task<HostLaunchOutcome> LaunchHostWithResolvedPathAsync(
        HostResolutionResult resolution,
        bool forceDirectMode,
        string? retryTag)
    {
        var hostPath = resolution.ResolvedHostPath!;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            EnsureExecutable(hostPath);
        }

        var hostWorkingDirectory = Path.GetDirectoryName(hostPath) ?? _deploymentLocator.GetAppRoot();
        var versionInfo = _deploymentLocator.GetVersionInfo();
        var forwardedArguments = BuildForwardedArguments(versionInfo);

        var primaryMode = forceDirectMode || !OperatingSystem.IsWindows()
            ? HostStartMode.Direct
            : HostStartMode.ShellExecute;
        var fallbackMode = primaryMode == HostStartMode.ShellExecute
            ? HostStartMode.Direct
            : (HostStartMode?)null;

        var firstAttempt = await StartHostProcessAsync(hostPath, hostWorkingDirectory, forwardedArguments, versionInfo, primaryMode, retryTag).ConfigureAwait(false);
        if (firstAttempt.ProcessCreated && !firstAttempt.ExitedEarly && firstAttempt.Process is not null)
        {
            var firstDetails = BuildResolutionDetails(resolution, firstAttempt, null, null);
            return HostLaunchOutcome.FromProcess(
                firstAttempt.Process,
                BuildResult(true, "launchHost", "ok", "Host launched.", firstDetails),
                firstDetails);
        }

        if (firstAttempt.ExitCode == HostExitCodes.SecondaryActivationSucceeded)
        {
            return BuildOutcomeFromAttempt(resolution, firstAttempt, null);
        }

        if (fallbackMode is null)
        {
            return BuildOutcomeFromAttempt(resolution, firstAttempt, null);
        }

        Logger.Warn(
            $"Primary host start attempt failed. Retrying with fallback mode '{fallbackMode}'. " +
            $"FailureReason='{firstAttempt.FailureReason ?? "unknown"}'; ExitCode='{firstAttempt.ExitCode?.ToString() ?? "<none>"}'.");

        var secondAttempt = await StartHostProcessAsync(hostPath, hostWorkingDirectory, forwardedArguments, versionInfo, fallbackMode.Value, retryTag).ConfigureAwait(false);
        if (secondAttempt.ProcessCreated && !secondAttempt.ExitedEarly && secondAttempt.Process is not null)
        {
            var details = BuildResolutionDetails(resolution, firstAttempt, secondAttempt, null);
            return HostLaunchOutcome.FromProcess(
                secondAttempt.Process,
                BuildResult(true, "launchHost", "ok", "Host launched.", details),
                details);
        }

        return BuildOutcomeFromAttempt(resolution, secondAttempt, firstAttempt);
    }

    private static HostLaunchOutcome BuildOutcomeFromAttempt(
        HostResolutionResult resolution,
        HostStartAttempt finalAttempt,
        HostStartAttempt? previousAttempt)
    {
        var details = BuildResolutionDetails(
            resolution,
            previousAttempt ?? finalAttempt,
            previousAttempt is null ? null : finalAttempt,
            !finalAttempt.ProcessCreated
                ? "start"
                : finalAttempt.ExitCode is HostExitCodes.SecondaryActivationFailed or HostExitCodes.RestartLockNotAcquired
                    ? "activation"
                    : "early-exit");

        if (!finalAttempt.ProcessCreated)
        {
            return HostLaunchOutcome.FromResult(BuildResult(
                false,
                "launchHost",
                "host_start_failed",
                $"Failed to start host using start mode '{finalAttempt.StartMode}'.",
                details));
        }

        if (finalAttempt.ExitCode == HostExitCodes.SecondaryActivationSucceeded)
        {
            return HostLaunchOutcome.FromImmediateResult(BuildResult(
                true,
                "launch",
                "activation_redirected",
                "Launcher activation was redirected to the existing desktop instance.",
                details));
        }

        if (finalAttempt.ExitCode is HostExitCodes.SecondaryActivationFailed or HostExitCodes.RestartLockNotAcquired)
        {
            return HostLaunchOutcome.FromResult(BuildResult(
                false,
                "launch",
                "activation_failed",
                $"Host activation handshake failed using start mode '{finalAttempt.StartMode}'.",
                details));
        }

        return HostLaunchOutcome.FromResult(BuildResult(
            false,
            "launchHost",
            "host_exited_early",
            $"Host exited early using start mode '{finalAttempt.StartMode}'.",
            details));
    }

    private async Task<HostStartAttempt> StartHostProcessAsync(
        string hostPath,
        string hostWorkingDirectory,
        string arguments,
        AppVersionInfo versionInfo,
        HostStartMode startMode,
        string? retryTag)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            WorkingDirectory = hostWorkingDirectory,
            Arguments = arguments,
            UseShellExecute = startMode == HostStartMode.ShellExecute
        };

        if (startMode == HostStartMode.Direct)
        {
            startInfo.EnvironmentVariables[LauncherIpcConstants.LauncherPidEnvVar] = Environment.ProcessId.ToString();
            startInfo.EnvironmentVariables[LauncherIpcConstants.PackageRootEnvVar] = _deploymentLocator.GetAppRoot();
            startInfo.EnvironmentVariables[LauncherIpcConstants.VersionEnvVar] = versionInfo.Version;
            startInfo.EnvironmentVariables[LauncherIpcConstants.CodenameEnvVar] = versionInfo.Codename;
        }

        try
        {
            var process = Process.Start(startInfo);
            Logger.Info(
                $"Host launch requested. Mode='{startMode}'; RetryTag='{retryTag ?? "<none>"}'; Path='{hostPath}'; " +
                $"WorkingDir='{hostWorkingDirectory}'; Pid={(process is null ? -1 : process.Id)}; Args='{startInfo.Arguments}'.");

            if (process is null)
            {
                return HostStartAttempt.StartFailed(startMode, "process_start_returned_null");
            }

            var exitTask = process.WaitForExitAsync();
            var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            if (completed == exitTask)
            {
                return HostStartAttempt.EarlyExit(startMode, process, process.ExitCode);
            }

            return HostStartAttempt.Started(startMode, process);
        }
        catch (Exception ex)
        {
            Logger.Error($"Host start failed. Mode='{startMode}'.", ex);
            return HostStartAttempt.StartFailed(startMode, ex.GetType().Name);
        }
    }

    private string BuildForwardedArguments(AppVersionInfo versionInfo)
    {
        var arguments = new System.Text.StringBuilder();

        for (var index = 0; index < _context.RawArgs.Count; index++)
        {
            var arg = _context.RawArgs[index];

            if (arg == _context.Command || arg == _context.SubCommand)
            {
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var key = arg[2..];
                var equalsIndex = key.IndexOf('=');
                if (equalsIndex >= 0)
                {
                    key = key[..equalsIndex];
                }

                if (LauncherOnlyOptions.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    if (equalsIndex < 0 &&
                        index + 1 < _context.RawArgs.Count &&
                        !_context.RawArgs[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        index++;
                    }

                    continue;
                }
            }

            if (arguments.Length > 0)
            {
                arguments.Append(' ');
            }

            arguments.Append(QuoteArgument(arg));
        }

        if (arguments.Length > 0)
        {
            arguments.Append(' ');
        }

        arguments.Append($"--{LauncherIpcConstants.LauncherPidEnvVar}={Environment.ProcessId}");
        arguments.Append($" --{LauncherIpcConstants.PackageRootEnvVar}={QuoteArgument(_deploymentLocator.GetAppRoot())}");
        arguments.Append($" --{LauncherIpcConstants.VersionEnvVar}={versionInfo.Version}");
        arguments.Append($" --{LauncherIpcConstants.CodenameEnvVar}={QuoteArgument(versionInfo.Codename)}");

        return arguments.ToString();
    }

    private async Task<(ErrorWindowResult Result, string? CustomPath)> ShowHostNotFoundErrorAsync()
    {
        ErrorWindow? errorWindow = null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                errorWindow = new ErrorWindow();
                errorWindow.ConfigureForHostNotFound();
                errorWindow.SetErrorMessage("LanMountainDesktop host executable was not found.");
                errorWindow.Show();
                Logger.Warn("Host not found. Showing error window.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show host-not-found error window.", ex);
            }
        });

        if (errorWindow is null)
        {
            return (ErrorWindowResult.Exit, null);
        }

        ErrorWindowResult result;
        string? customPath;
        try
        {
            result = await errorWindow.WaitForChoiceAsync().ConfigureAwait(false);
            customPath = errorWindow.GetCustomHostPath();
            Logger.Info($"Host-not-found window result='{result}'; HasCustomPath={!string.IsNullOrWhiteSpace(customPath)}.");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed while waiting for host-not-found window result.", ex);
            result = ErrorWindowResult.Exit;
            customPath = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (errorWindow.IsVisible && errorWindow.IsLoaded)
                {
                    errorWindow.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to close host-not-found error window.", ex);
            }
        });

        return (result, customPath);
    }

    private async Task<MigrationResult> ShowMigrationPromptAsync(LegacyVersionInfo legacyInfo)
    {
        MigrationPromptWindow? migrationWindow = null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                migrationWindow = new MigrationPromptWindow();
                migrationWindow.SetLegacyInfo(legacyInfo);
                migrationWindow.Show();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show migration prompt window.", ex);
            }
        });

        if (migrationWindow is null)
        {
            return MigrationResult.Skipped;
        }

        MigrationResult result;
        try
        {
            result = await migrationWindow.WaitForChoiceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed while waiting for migration prompt result.", ex);
            result = MigrationResult.Skipped;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (migrationWindow.IsVisible && migrationWindow.IsLoaded)
                {
                    migrationWindow.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to close migration prompt window.", ex);
            }
        });

        return result;
    }

    private static string MapStartupStageToSplashStage(StartupStage stage) => stage switch
    {
        StartupStage.Initializing => "initializing",
        StartupStage.LoadingSettings => "settings",
        StartupStage.LoadingPlugins => "plugins",
        StartupStage.TrayReady => "shell",
        StartupStage.InitializingUI => "ui",
        StartupStage.ShellInitialized => "shell",
        StartupStage.BackgroundReady => "ready",
        StartupStage.DesktopVisible => "ready",
        StartupStage.ActivationRedirected => "activation",
        StartupStage.ActivationFailed => "error",
        StartupStage.Ready => "ready",
        _ => "launch"
    };

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

    private static Dictionary<string, string> BuildResolutionDetails(
        HostResolutionResult resolution,
        HostStartAttempt? firstAttempt,
        HostStartAttempt? secondAttempt,
        string? failureStage)
    {
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedAppRoot"] = resolution.AppRoot,
            ["explicitAppRoot"] = resolution.ExplicitAppRoot ?? string.Empty,
            ["resolvedHostPath"] = resolution.ResolvedHostPath ?? string.Empty,
            ["resolutionSource"] = resolution.ResolutionSource ?? string.Empty,
            ["devModeConfigIgnored"] = resolution.DevModeConfigIgnored.ToString(),
            ["searchedPaths"] = string.Join(" | ", resolution.SearchedPaths),
            ["failureStage"] = failureStage ?? string.Empty
        };

        if (firstAttempt is not null)
        {
            details["startMode"] = firstAttempt.StartMode.ToString();
            details["processCreated"] = firstAttempt.ProcessCreated.ToString();
            details["hostPid"] = firstAttempt.ProcessId?.ToString() ?? string.Empty;
            details["firstAttemptFailureReason"] = firstAttempt.FailureReason ?? string.Empty;
            details["firstAttemptExitCode"] = firstAttempt.ExitCode?.ToString() ?? string.Empty;
        }

        if (secondAttempt is not null)
        {
            details["fallbackStartMode"] = secondAttempt.StartMode.ToString();
            details["fallbackProcessCreated"] = secondAttempt.ProcessCreated.ToString();
            details["fallbackHostPid"] = secondAttempt.ProcessId?.ToString() ?? string.Empty;
            details["fallbackFailureReason"] = secondAttempt.FailureReason ?? string.Empty;
            details["fallbackExitCode"] = secondAttempt.ExitCode?.ToString() ?? string.Empty;
        }

        return details;
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

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Contains('"') && !value.Contains(' ') && !value.Contains('\t'))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder();
        builder.Append('"');
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                builder.Append("\\\"");
            }
            else
            {
                builder.Append(ch);
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }
        catch
        {
        }
    }

    private static async Task<bool> TryConnectToPublicIpcAsync(
        LanMountainDesktopIpcClient ipcClient,
        TimeSpan timeout)
    {
        var connectTask = ipcClient.ConnectAsync();
        var completedTask = await Task.WhenAny(connectTask, Task.Delay(timeout)).ConfigureAwait(false);
        if (completedTask != connectTask)
        {
            return false;
        }

        await connectTask.ConfigureAwait(false);
        return true;
    }

    private static async Task<StartupSuccessState?> TryRecoverWithPublicActivationAsync(
        LanMountainDesktopIpcClient ipcClient,
        Process hostProcess,
        Task<StartupSuccessState> successTask,
        StartupSuccessTracker startupSuccessTracker)
    {
        try
        {
            var shellProxy = ipcClient.CreateProxy<IPublicShellControlService>();
            var activationAccepted = await shellProxy.ActivateMainWindowAsync().ConfigureAwait(false);
            if (!activationAccepted)
            {
                return null;
            }

            var completedTask = await Task.WhenAny(successTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            if (completedTask == successTask)
            {
                return await successTask.ConfigureAwait(false);
            }

            if (!hostProcess.HasExited)
            {
                return startupSuccessTracker.BuildRecoverySuccessState();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Public activation recovery failed: {ex.Message}");
        }

        return null;
    }

    private static LoadingStateMessage BuildDelayedLoadingState(
        LoadingStateMessage loadingState,
        string summaryMessage,
        string detailMessage,
        DateTimeOffset startedAtUtc)
    {
        var delayedItems = loadingState.ActiveItems
            .Where(item => !string.Equals(item.Id, "launcher-soft-timeout", StringComparison.OrdinalIgnoreCase))
            .ToList();

        delayedItems.Insert(0, new LoadingItem
        {
            Id = "launcher-soft-timeout",
            Type = LoadingItemType.System,
            Name = "Startup still in progress",
            Description = detailMessage,
            State = LoadingState.Delayed,
            ProgressPercent = Math.Max(loadingState.OverallProgressPercent, 1),
            Message = detailMessage,
            StartTime = startedAtUtc
        });

        return loadingState with
        {
            ActiveItems = delayedItems,
            Message = summaryMessage,
            Timestamp = DateTimeOffset.UtcNow,
            TotalCount = Math.Max(loadingState.TotalCount, delayedItems.Count)
        };
    }

    private static Dictionary<string, string> BuildAttemptDetails(
        StartupAttemptRecord? trackedAttempt,
        bool attachedToExistingAttempt,
        bool ipcConnected,
        bool hostProcessAlive,
        StartupStage lastStage,
        string lastStageMessage,
        string? activationFailureReason,
        bool softTimeoutShown,
        bool recoveryActivationAttempted)
    {
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hostProcessAlive"] = hostProcessAlive.ToString(),
            ["attachedToExistingAttempt"] = attachedToExistingAttempt.ToString(),
            ["ipcConnected"] = ipcConnected.ToString(),
            ["ipcStage"] = lastStage.ToString(),
            ["ipcMessage"] = lastStageMessage,
            ["activationFailureReason"] = activationFailureReason ?? string.Empty,
            ["softTimeoutShown"] = softTimeoutShown.ToString(),
            ["recoveryActivationAttempted"] = recoveryActivationAttempted.ToString()
        };

        if (trackedAttempt is not null)
        {
            details["startupAttemptId"] = trackedAttempt.AttemptId;
            details["startupAttemptState"] = trackedAttempt.State.ToString();
            details["startupAttemptStartedAtUtc"] = trackedAttempt.StartedAtUtc.ToString("O");
            details["startupAttemptUpdatedAtUtc"] = trackedAttempt.UpdatedAtUtc.ToString("O");
            details["successPolicy"] = trackedAttempt.SuccessPolicy;
            details["hostPid"] = trackedAttempt.HostPid.ToString();
        }

        return details;
    }

    private static bool TryGetLiveProcess(int processId, out Process? process)
    {
        process = null;
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            process?.Dispose();
            process = null;
            return false;
        }
    }

    private enum HostStartMode
    {
        ShellExecute,
        Direct
    }

    private sealed record HostStartAttempt(
        HostStartMode StartMode,
        bool ProcessCreated,
        Process? Process,
        bool ExitedEarly,
        int? ExitCode,
        string? FailureReason)
    {
        public int? ProcessId => Process?.Id;

        public static HostStartAttempt Started(HostStartMode startMode, Process process) =>
            new(startMode, true, process, false, null, null);

        public static HostStartAttempt EarlyExit(HostStartMode startMode, Process process, int exitCode) =>
            new(startMode, true, process, true, exitCode, null);

        public static HostStartAttempt StartFailed(HostStartMode startMode, string failureReason) =>
            new(startMode, false, null, false, null, failureReason);
    }

    private sealed record HostLaunchOutcome(
        LauncherResult Result,
        Process? Process,
        LauncherResult? ImmediateResult,
        Dictionary<string, string> Details)
    {
        public static HostLaunchOutcome FromResult(LauncherResult result) =>
            new(result, null, result.Success ? result : null, result.Details);

        public static HostLaunchOutcome FromImmediateResult(LauncherResult result) =>
            new(result, null, result, result.Details);

        public static HostLaunchOutcome FromProcess(Process process, LauncherResult result, Dictionary<string, string> details) =>
            new(result, process, null, details);
    }

    private sealed class StartupSuccessTracker
    {
        private readonly LaunchSuccessPolicy _policy;
        private bool _trayReady;
        private bool _backgroundReady;

        public string PolicyKey => _policy.ToString();

        public StartupSuccessTracker(CommandContext context)
        {
            var restartPresentation = LauncherRuntimeMetadata.GetRestartPresentationMode(context.RawArgs);
            var isRestartLaunch = string.Equals(context.LaunchSource, "restart", StringComparison.OrdinalIgnoreCase);

            _policy = !isRestartLaunch
                ? LaunchSuccessPolicy.Foreground
                : restartPresentation switch
                {
                    RestartPresentationMode.Tray => LaunchSuccessPolicy.RestartTray,
                    RestartPresentationMode.Minimized => LaunchSuccessPolicy.RestartBackground,
                    _ => LaunchSuccessPolicy.Foreground
                };
        }

        public bool TryResolve(StartupStage stage, out StartupSuccessState successState)
        {
            switch (stage)
            {
                case StartupStage.ActivationRedirected:
                    successState = new StartupSuccessState(
                        stage,
                        "activation_redirected",
                        "Launcher activation was redirected to the existing desktop instance.");
                    return true;

                case StartupStage.DesktopVisible:
                    successState = new StartupSuccessState(
                        stage,
                        _policy == LaunchSuccessPolicy.Foreground ? "ok" : "desktop_visible_fallback",
                        _policy == LaunchSuccessPolicy.Foreground
                            ? "Desktop is visible and ready."
                            : "Desktop recovered in a visible state.");
                    return true;

                case StartupStage.TrayReady:
                    _trayReady = true;
                    break;

                case StartupStage.BackgroundReady:
                    _backgroundReady = true;
                    break;
            }

            if (_policy == LaunchSuccessPolicy.RestartBackground && _backgroundReady)
            {
                successState = new StartupSuccessState(
                    StartupStage.BackgroundReady,
                    "background_ready",
                    "Desktop restart completed in the background.");
                return true;
            }

            if (_policy == LaunchSuccessPolicy.RestartTray && _trayReady && _backgroundReady)
            {
                successState = new StartupSuccessState(
                    StartupStage.BackgroundReady,
                    "background_ready",
                    "Desktop restart completed with tray recovery ready.");
                return true;
            }

            successState = default!;
            return false;
        }

        public StartupSuccessState BuildRecoverySuccessState()
        {
            return _policy switch
            {
                LaunchSuccessPolicy.RestartTray => new StartupSuccessState(
                    StartupStage.DesktopVisible,
                    "recovery_activation_requested",
                    "Launcher requested a visible recovery because the background restart never confirmed tray readiness."),
                LaunchSuccessPolicy.RestartBackground => new StartupSuccessState(
                    StartupStage.DesktopVisible,
                    "recovery_activation_requested",
                    "Launcher requested a visible recovery because the background restart never confirmed readiness."),
                _ => new StartupSuccessState(
                    StartupStage.DesktopVisible,
                    "recovery_activation_requested",
                    "Launcher requested a visible recovery from the running desktop instance.")
            };
        }
    }

    private sealed record StartupSuccessState(
        StartupStage Stage,
        string Code,
        string Message);

    private enum LaunchSuccessPolicy
    {
        Foreground,
        RestartBackground,
        RestartTray
    }
}
