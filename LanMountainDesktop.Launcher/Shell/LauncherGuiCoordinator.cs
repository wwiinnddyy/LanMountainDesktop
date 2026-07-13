using System.Diagnostics;
using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Resources;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Shell;

internal static class LauncherGuiCoordinator
{
    public static async Task RunAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        CommandContext context,
        SplashWindow splashWindow)
    {
        LauncherResult result;
        SplashWindow? currentSplashWindow = splashWindow;
        var appRoot = Commands.ResolveAppRoot(context);
        var dataLocationResolver = new DataLocationResolver(appRoot);
        var startupAttemptRegistry = new StartupAttemptRegistry();
        var coordinatorPipeName = LauncherCoordinatorIpcServer.CreatePipeName();
        var successPolicy = LauncherOrchestrator.ResolveSuccessPolicyKey(context);

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

                var orchestrator = LauncherCompositionRoot.CreateOrchestrator(
                    context,
                    appRoot,
                    startupAttemptRegistry,
                    coordinatorServer);

                result = await orchestrator.RunAsync(currentSplashWindow).ConfigureAwait(false);
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
        Environment.ExitCode = result.Success ? 0 : 1;
        if (result.Success)
        {
            var hostPid = ResolveManagedHostPid(result, startupAttemptRegistry.GetOwnedAttempt()?.HostPid ?? 0);
            try
            {
                var airAppRuntimeBridge = new AirAppRuntimeBridge(appRoot, dataLocationResolver.ResolveDataRoot());
                var handoff = await airAppRuntimeBridge.AttachHostAsync(hostPid).ConfigureAwait(false);
                RecordAirAppRuntimeHandoff(result, handoff);
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"AIRAPP: Unexpected runtime ownership handoff failure; Host fallback remains available. " +
                    $"HostPid={hostPid}; Error='{ex.Message}'.");
                result.Details["airAppRuntimeHandoffAccepted"] = bool.FalseString;
                result.Details["airAppRuntimeHandoffCode"] = "handoff_exception";
                result.Details["airAppRuntimeHandoffMessage"] = ex.Message;
                result.Details["airAppRuntimeHostPid"] = hostPid.ToString();
                result.Details["airAppRuntimeAttachAttempts"] = "0";
            }
        }

        await WriteLauncherResultAsync(context, result).ConfigureAwait(false);
        Logger.Info("Launcher coordination is complete; shutting down without extending the Host process lifetime.");
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(Environment.ExitCode), DispatcherPriority.Background);
    }

    private static SplashWindow CreateSplashWindow()
    {
        var window = new SplashWindow();
        TrySetSplashVersionInfo(window, LauncherRuntimeContext.Current);
        return window;
    }

    private static void TrySetSplashVersionInfo(SplashWindow window, CommandContext context)
    {
        try
        {
            var appRoot = Commands.ResolveAppRoot(context);
            var versionInfo = new DeploymentLocator(appRoot).GetVersionInfo();
            window.SetVersionInfo(versionInfo.Version, versionInfo.Codename);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to set splash version info before coordinator start: {ex.Message}");
        }
    }

    private static int ResolveManagedHostPid(LauncherResult result, int fallbackHostPid)
    {
        if (result.Details.TryGetValue("hostPid", out var hostPidText) &&
            int.TryParse(hostPidText, out var hostPid))
        {
            return hostPid;
        }

        if (result.Details.TryGetValue("existingHostPid", out var existingHostPidText) &&
            int.TryParse(existingHostPidText, out var existingHostPid))
        {
            return existingHostPid;
        }

        return fallbackHostPid;
    }

    private static void RecordAirAppRuntimeHandoff(
        LauncherResult result,
        AirAppRuntimeHandoffResult handoff)
    {
        result.Details["airAppRuntimeHandoffAccepted"] = handoff.Accepted.ToString();
        result.Details["airAppRuntimeHandoffCode"] = handoff.Code;
        result.Details["airAppRuntimeHandoffMessage"] = handoff.Message;
        result.Details["airAppRuntimeHostPid"] = handoff.HostProcessId.ToString();
        result.Details["airAppRuntimeAttachAttempts"] = handoff.Attempts.ToString();
        result.Details["airAppRuntimeProcessId"] = handoff.Status?.ProcessId.ToString() ?? string.Empty;
    }

    private static async Task<LauncherResult> AttachToExistingCoordinatorAsync(
        CommandContext context,
        SplashWindow? splashWindow,
        StartupAttemptRecord? activeCoordinatorAttempt)
    {
        var reporter = splashWindow as ISplashStageReporter;
        reporter?.Report("activation", Strings.Preview_ActivationConnecting);

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
                SuccessPolicy = LauncherOrchestrator.ResolveSuccessPolicyKey(context)
            };

            var response = await new LauncherCoordinatorIpcClient()
                .SendAsync(activeCoordinatorAttempt.CoordinatorPipeName, request, TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);

            if (response is not null)
            {
                reporter?.Report("activation", response.Message);
                await DismissSplashIfNeededAsync(splashWindow).ConfigureAwait(false);
                var success = response.Accepted ||
                              IsRecoverableActivationFailure(response.ActivationResult, response.Status);
                return new LauncherResult
                {
                    Success = success,
                    Stage = "launch",
                    Code = success && !response.Accepted ? "attached_to_launcher_coordinator" : response.Code,
                    Message = success && !response.Accepted
                        ? "Attached to the active Launcher coordinator; desktop startup is still in progress."
                        : response.Message,
                    Details = BuildCoordinatorResultDetails(response.Status, response.ActivationResult)
                };
            }
        }

        var activation = await TryActivateExistingInstanceWithStatusAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        if (activation is not null)
        {
            reporter?.Report("activation", activation.Message);
            await DismissSplashIfNeededAsync(splashWindow).ConfigureAwait(false);
            var success = activation.Accepted || IsRecoverableActivationFailure(activation, null);
            return new LauncherResult
            {
                Success = success,
                Stage = "launch",
                Code = activation.Accepted
                    ? "existing_host_activated"
                    : success
                        ? "existing_host_startup_pending"
                        : "existing_host_activation_failed",
                Message = success && !activation.Accepted
                    ? "Existing desktop process is still starting; Launcher attached without starting another process."
                    : activation.Message,
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
                if (!activation.Accepted && IsRecoverableActivationFailure(activation, status))
                {
                    return new LauncherCoordinatorResponse
                    {
                        Accepted = true,
                        Code = "attached_to_launcher_coordinator",
                        Message = "Attached to the active Launcher coordinator; desktop startup is still in progress.",
                        Status = status,
                        ActivationResult = activation
                    };
                }

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

    private static bool IsRecoverableActivationFailure(
        PublicShellActivationResult? activation,
        LauncherCoordinatorStatus? status)
    {
        if (activation is { Accepted: true })
        {
            return false;
        }

        if (status is { Completed: false, HostProcessAlive: true })
        {
            return true;
        }

        var shellStatus = activation?.Status;
        if (shellStatus is null || !shellStatus.PublicIpcReady)
        {
            return false;
        }

        return !shellStatus.MainWindowOpened ||
               !shellStatus.DesktopVisible ||
               string.Equals(activation?.Code, "shell_not_ready", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(activation?.Code, "startup_pending", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildCoordinatorResultDetails(
        LauncherCoordinatorStatus? status,
        PublicShellActivationResult? activation)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

        // 读取主程序崩溃转储，获取真实崩溃原因
        var crashDump = ReadLatestHostCrashDump();

        // 读取主程序 stderr 输出（来自启动器捕获）
        var stderrOutput = ExtractHostStderr(result);

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

                var fullMessage = $"Failed to start LanMountainDesktop.\n\nStage: {result.Stage}\nCode: {result.Code}\n\n{result.Message}";

                if (!string.IsNullOrWhiteSpace(stderrOutput))
                {
                    fullMessage += $"\n\n--- Host Output ---\n{stderrOutput}";
                }

                if (!string.IsNullOrWhiteSpace(crashDump))
                {
                    fullMessage += $"\n\n--- Host Crash Details ---\n{crashDump}";
                }

                errorWindow.SetErrorMessage(fullMessage);
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

    /// <summary>
    /// 从 LauncherResult.Details 中提取主程序的 stderr 输出。
    /// 启动器在 Direct 模式下会重定向主程序的 stderr 并存入 details。
    /// </summary>
    private static string? ExtractHostStderr(LauncherResult result)
    {
        // 优先使用 fallback 尝试的 stderr（因为 fallback 通常是 ShellExecute，stderr 不可用）
        // 所以优先使用 firstAttemptStderr
        var stderrKeys = new[] { "firstAttemptStderr", "fallbackAttemptStderr" };
        foreach (var key in stderrKeys)
        {
            if (result.Details.TryGetValue(key, out var stderr) && !string.IsNullOrWhiteSpace(stderr))
            {
                // 限制长度避免错误窗口过长
                var trimmed = stderr.Trim();
                if (trimmed.Length > 2000)
                {
                    trimmed = trimmed.Substring(0, 2000) + "\n... (truncated)";
                }
                return trimmed;
            }
        }
        return null;
    }

    /// <summary>
    /// 读取主程序最新的崩溃转储文件内容。
    /// 主程序在崩溃时会写入 LocalApplicationData/LanMountainDesktop/crashes/ 目录。
    /// 启动器读取最近 5 分钟内的崩溃转储，避免显示过时的崩溃信息。
    /// </summary>
    private static string? ReadLatestHostCrashDump()
    {
        try
        {
            var crashDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanMountainDesktop", "crashes");

            if (!Directory.Exists(crashDir))
            {
                return null;
            }

            // 优先读取 latest.txt 标记文件指向的崩溃转储
            var latestMarker = Path.Combine(crashDir, "latest.txt");
            string? targetCrashFile = null;

            if (File.Exists(latestMarker))
            {
                var referencedPath = File.ReadAllText(latestMarker).Trim();
                if (File.Exists(referencedPath))
                {
                    var fileInfo = new FileInfo(referencedPath);
                    if (fileInfo.CreationTime > DateTime.Now.AddMinutes(-5))
                    {
                        targetCrashFile = referencedPath;
                    }
                }
            }

            // 回退：查找最近 5 分钟内的崩溃转储文件
            if (targetCrashFile is null)
            {
                var recentCrash = Directory.GetFiles(crashDir, "crash-*.txt")
                    .Select(f => new FileInfo(f))
                    .Where(f => f.CreationTime > DateTime.Now.AddMinutes(-5))
                    .OrderByDescending(f => f.CreationTime)
                    .FirstOrDefault();

                targetCrashFile = recentCrash?.FullName;
            }

            if (string.IsNullOrWhiteSpace(targetCrashFile) || !File.Exists(targetCrashFile))
            {
                return null;
            }

            var content = File.ReadAllText(targetCrashFile);
            Logger.Info($"Read host crash dump: {targetCrashFile}");
            return content;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to read host crash dump: {ex.Message}");
            return null;
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
}
