using System.Diagnostics;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services.Ipc;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class LauncherFlowCoordinator
{
    private static readonly string[] LauncherOnlyOptions =
    [
        "debug", "show-loading-details", "plugins-dir", "source", "result",
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
        _oobeSteps = [new WelcomeOobeStep(_oobeStateService)];
    }

    public async Task<LauncherResult> RunAsync(SplashWindow? existingSplashWindow = null)
    {
        try
        {
            // 清理旧版本，保留至少3个版本
            _deploymentLocator.CleanupOldDeployments(minVersionsToKeep: 3);

            // 检测老版本安装（首次运行时）
            if (_oobeStateService.IsFirstRun())
            {
                var legacyInfo = LegacyVersionDetector.DetectLegacyInstallation();
                if (legacyInfo != null)
                {
                    var migrationResult = await ShowMigrationPromptAsync(legacyInfo);
                    // 无论用户选择什么，都继续启动流程
                    Console.WriteLine($"[LauncherFlowCoordinator] Migration prompt result: {migrationResult}");
                }
            }

            // 使用传入的 Splash 窗口或创建新的
            var splashWindow = existingSplashWindow ?? await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new SplashWindow();
                window.Show();
                return window;
            });

            var reporter = (ISplashStageReporter)splashWindow;
            
            // 创建加载详情窗口（可选，用于显示详细加载状态）
            LoadingDetailsWindow? loadingDetailsWindow = null;
            if (_context.IsDebugMode || _context.GetOption("show-loading-details") == "true")
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    loadingDetailsWindow = new LoadingDetailsWindow();
                    loadingDetailsWindow.Show();
                });
            }
            
            // 跟踪主程序是否已就绪，就绪后自动关闭 Splash 窗口
            var hostReadyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            
            // 加载状态管理
            var loadingState = new LoadingStateMessage();
            
            // 启动 IPC 服务端监听主程序进度
            using var ipcServer = new LauncherIpcServer(msg =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        // 更新加载状态
                        loadingState = loadingState with
                        {
                            Stage = msg.Stage,
                            OverallProgressPercent = msg.ProgressPercent,
                            Message = msg.Message,
                            Timestamp = DateTimeOffset.UtcNow
                        };
                        
                        // 报告到 Splash 窗口
                        reporter.Report(msg.Stage.ToString().ToLower(), msg.Message ?? "");
                        
                        // 更新加载详情窗口
                        loadingDetailsWindow?.UpdateLoadingState(loadingState);
                        
                        // 主程序报告就绪后，关闭 Splash 窗口和加载详情窗口
                        if (msg.Stage == StartupStage.Ready)
                        {
                            if (splashWindow.IsVisible && splashWindow.IsLoaded)
                            {
                                splashWindow.Close();
                            }
                            loadingDetailsWindow?.Close();
                            hostReadyTcs.TrySetResult();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[LauncherFlowCoordinator] Error in IPC callback: {ex.Message}");
                    }
                });
            });
            ipcServer.Start();

            try
            {
                // 检查并安装待处理的更新（主程序下载的）
                reporter.Report("update", "检查更新...");
                var updateResult = await _updateEngine.ApplyPendingUpdateAsync().ConfigureAwait(false);
                if (!updateResult.Success)
                {
                    return updateResult;
                }

                // 检查并安装待处理的插件更新
                reporter.Report("plugins", "检查插件更新...");
                var pluginsDir = _context.GetOption("plugins-dir")
                                 ?? Path.Combine(_deploymentLocator.GetAppRoot(), "plugins");
                var queueResult = new PluginUpgradeQueueService(_pluginInstallerService).ApplyPendingUpgrades(pluginsDir);
                if (!queueResult.Success)
                {
                    return queueResult;
                }

                // OOBE（首次运行引导）
                if (_oobeStateService.IsFirstRun())
                {
                    await Dispatcher.UIThread.InvokeAsync(() => splashWindow.Hide());
                    foreach (var step in _oobeSteps)
                    {
                        await step.RunAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    await Dispatcher.UIThread.InvokeAsync(() => splashWindow.Show());
                }

                // 启动主程序
                reporter.Report("launch", "正在启动...");
                var (hostResult, hostProcess) = await LaunchHostWithIpcAsync(splashWindow);
                if (!hostResult.Success)
                {
                    return hostResult;
                }

                // 等待主程序进程退出。Launcher 作为后台守护进程保持运行，
                // 维持 IPC 管道服务端供主程序报告启动进度。
                if (hostProcess is not null)
                {
                    var processExitTask = hostProcess.WaitForExitAsync();
                    
                    // 等待主程序就绪或进程退出（取先发生者）
                    // 30 秒超时，宿主端有 10 秒兜底机制确保 Ready 信号发送
                    var readyOrTimeoutOrExit = Task.WhenAny(
                        hostReadyTcs.Task,
                        processExitTask,
                        Task.Delay(TimeSpan.FromSeconds(30)));
                    
                    var completedTask = await readyOrTimeoutOrExit;
                    
                    // Host process exited before reporting Ready.
                    if (completedTask == processExitTask)
                    {
                        var exitCode = hostProcess.ExitCode;
                        Console.Error.WriteLine($"[LauncherFlowCoordinator] Host process exited before Ready. ExitCode={exitCode}.");

                        var recoveryResult = await TryRecoverFromEarlyHostExitAsync(
                            exitCode,
                            hostReadyTcs,
                            splashWindow,
                            loadingDetailsWindow).ConfigureAwait(false);
                        if (recoveryResult is not null)
                        {
                            return recoveryResult;
                        }

                        // Close Splash window for unrecoverable early exits.
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
                                if (splashWindow.IsVisible && splashWindow.IsLoaded)
                                {
                                    splashWindow.Close();
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[LauncherFlowCoordinator] Error closing splash window: {ex.Message}");
                            }
                        });
                            
                        return new LauncherResult
                        {
                            Success = false,
                            Stage = "launch",
                            Code = "host_crashed",
                            Message = $"主程序异常退出，退出代码: {exitCode}"
                        };
                    }
                    
                    // 如果 Splash 窗口仍然打开（超时情况），关闭它
                    if (splashWindow.IsVisible)
                    {
                        Console.WriteLine("[LauncherFlowCoordinator] Timeout waiting for Ready signal, closing splash window...");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
                                if (splashWindow.IsVisible && splashWindow.IsLoaded)
                                {
                                    splashWindow.Close();
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[LauncherFlowCoordinator] Error closing splash window on timeout: {ex.Message}");
                            }
                        });
                    }
                    
                    // 继续等待主程序进程退出（如果它还在运行）
                    if (!hostProcess.HasExited)
                    {
                        await processExitTask;
                    }
                }
                else
                {
                    // 如果无法获取进程引用，退回到有限等待
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }

                return new LauncherResult
                {
                    Success = true,
                    Stage = "exit",
                    Code = "ok",
                    Message = "Launcher completed successfully."
                };
            }
            finally
            {
                // Splash 窗口可能已由 IPC Ready 回调关闭，这里做安全清理
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        if (splashWindow.IsVisible && splashWindow.IsLoaded)
                        {
                            splashWindow.Close();
                            Console.WriteLine("[LauncherFlowCoordinator] Splash window closed in finally block");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[LauncherFlowCoordinator] Error closing splash window in finally: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            return new LauncherResult
            {
                Success = false,
                Stage = "launch",
                Code = "exception",
                Message = ex.Message,
                ErrorMessage = ex.ToString()
            };
        }
    }

    private async Task<LauncherResult?> TryRecoverFromEarlyHostExitAsync(
        int exitCode,
        TaskCompletionSource hostReadyTcs,
        SplashWindow splashWindow,
        LoadingDetailsWindow? loadingDetailsWindow)
    {
        if (exitCode == HostExitCodes.SecondaryActivationSucceeded)
        {
            Console.WriteLine("[LauncherFlowCoordinator] Host redirected activation to an existing primary instance.");
            await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
            return new LauncherResult
            {
                Success = true,
                Stage = "launch",
                Code = "activated_existing_instance",
                Message = "Detected existing running instance and activation was acknowledged."
            };
        }

        if (exitCode is not HostExitCodes.SecondaryActivationFailed and not HostExitCodes.RestartLockNotAcquired)
        {
            return null;
        }

        Console.Error.WriteLine(
            $"[LauncherFlowCoordinator] Activation handshake failed with exit code {exitCode}. Retrying explicit activation once...");

        var (retryLaunchResult, retryProcess) = await LaunchHostWithIpcAsync(splashWindow).ConfigureAwait(false);
        if (!retryLaunchResult.Success)
        {
            return retryLaunchResult;
        }

        if (retryProcess is null)
        {
            return new LauncherResult
            {
                Success = false,
                Stage = "launch",
                Code = "activation_retry_start_failed",
                Message = "Explicit activation retry failed because no host process was created."
            };
        }

        Console.WriteLine($"[LauncherFlowCoordinator] Explicit activation retry started. RetryPid={retryProcess.Id}.");
        var retryExitTask = retryProcess.WaitForExitAsync();
        var retryCompleted = await Task.WhenAny(
            hostReadyTcs.Task,
            retryExitTask,
            Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false);

        if (retryCompleted == hostReadyTcs.Task)
        {
            Console.WriteLine("[LauncherFlowCoordinator] Host reported Ready after explicit activation retry.");
            await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
            return new LauncherResult
            {
                Success = true,
                Stage = "launch",
                Code = "activation_retry_ready",
                Message = "Explicit activation retry succeeded and host reported Ready."
            };
        }

        if (retryCompleted == retryExitTask)
        {
            var retryExitCode = retryProcess.ExitCode;
            if (retryExitCode == HostExitCodes.SecondaryActivationSucceeded)
            {
                await CloseWindowsAsync(splashWindow, loadingDetailsWindow).ConfigureAwait(false);
                return new LauncherResult
                {
                    Success = true,
                    Stage = "launch",
                    Code = "activation_retry_redirected",
                    Message = "Explicit activation retry redirected to the existing primary instance."
                };
            }

            return new LauncherResult
            {
                Success = false,
                Stage = "launch",
                Code = "activation_retry_failed",
                Message = $"Explicit activation retry failed. ExitCode={retryExitCode}. 请结束残留后台进程后重试。"
            };
        }

        return new LauncherResult
        {
            Success = false,
            Stage = "launch",
            Code = "activation_retry_timeout",
            Message = "Explicit activation retry timed out before host became ready. 请结束残留后台进程后重试。"
        };
    }

    private static async Task CloseWindowsAsync(SplashWindow splashWindow, LoadingDetailsWindow? loadingDetailsWindow)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (splashWindow.IsVisible && splashWindow.IsLoaded)
                {
                    splashWindow.Close();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LauncherFlowCoordinator] Failed to close splash window: {ex.Message}");
            }

            try
            {
                if (loadingDetailsWindow is not null && loadingDetailsWindow.IsVisible)
                {
                    loadingDetailsWindow.Close();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LauncherFlowCoordinator] Failed to close loading details window: {ex.Message}");
            }
        });
    }

    private async Task<(LauncherResult Result, Process? Process)> LaunchHostWithIpcAsync(SplashWindow? splashWindow = null, string? customHostPath = null)
    {
        // 优先使用自定义路径（调试模式选择的路径）
        var hostPath = customHostPath ?? _deploymentLocator.ResolveHostExecutablePath();
        
        if (string.IsNullOrWhiteSpace(hostPath))
        {
            // 关闭 Splash 窗口
            // 显示错误窗口而不是直接退出
            var (errorResult, selectedPath) = await ShowHostNotFoundErrorAsync();
            
            if (errorResult == ErrorWindowResult.Retry)
            {
                // 用户选择重试，如果有选择路径则使用，否则重新尝试
                if (!string.IsNullOrWhiteSpace(selectedPath))
                {
                    return await LaunchHostWithIpcAsync(splashWindow, selectedPath);
                }
                return await LaunchHostWithIpcAsync(splashWindow);
            }
            
            // 用户选择退出
            return (new LauncherResult
            {
                Success = false,
                Stage = "launchHost",
                Code = "host_not_found",
                Message = "LanMountainDesktop host executable not found."
            }, null);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            EnsureExecutable(hostPath);
        }

        var hostWorkingDir = Path.GetDirectoryName(hostPath) ?? _deploymentLocator.GetAppRoot();
        var versionInfo = _deploymentLocator.GetVersionInfo();

        // 构建命令行参数：转发用户参数 + IPC 环境信息通过命令行传递
        // UseShellExecute = true 确保 Shell 启动子进程，使其正确关联到交互式桌面窗口站(WinSta0)，
        // 避免子进程窗口创建成功但不可见的问题。
        var arguments = new System.Text.StringBuilder();

        // 转发命令行参数给主程序（排除 Launcher 自己的命令和选项）
        // 只过滤 Launcher 专属的选项，保留宿主程序需要的参数（如 --restart-parent-pid）
        foreach (var arg in _context.RawArgs)
        {
            if (arg == _context.Command || arg == _context.SubCommand)
                continue;
            
            if (arg.StartsWith("--"))
            {
                var key = arg[2..];
                var equalsIndex = key.IndexOf('=');
                if (equalsIndex >= 0) key = key[..equalsIndex];
                
                if (LauncherOnlyOptions.Contains(key, StringComparer.OrdinalIgnoreCase))
                    continue;
            }
            
            if (arguments.Length > 0) arguments.Append(' ');
            arguments.Append(QuoteArgument(arg));
        }

        // 通过命令行参数传递 IPC 连接信息（UseShellExecute=true 时不支持 EnvironmentVariables）
        if (arguments.Length > 0) arguments.Append(' ');
        arguments.Append($"--{LauncherIpcConstants.LauncherPidEnvVar}={Environment.ProcessId}");
        arguments.Append($" --{LauncherIpcConstants.PackageRootEnvVar}={QuoteArgument(_deploymentLocator.GetAppRoot())}");
        arguments.Append($" --{LauncherIpcConstants.VersionEnvVar}={versionInfo.Version}");
        arguments.Append($" --{LauncherIpcConstants.CodenameEnvVar}={versionInfo.Codename}");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            UseShellExecute = true,
            WorkingDirectory = hostWorkingDir,
            Arguments = arguments.ToString()
        };

        // 同时设置环境变量作为备选（当 UseShellExecute=true 时 EnvironmentVariables 仍会被子进程继承）
        processStartInfo.EnvironmentVariables[LauncherIpcConstants.LauncherPidEnvVar] = 
            Environment.ProcessId.ToString();
        processStartInfo.EnvironmentVariables[LauncherIpcConstants.PackageRootEnvVar] = 
            _deploymentLocator.GetAppRoot();
        processStartInfo.EnvironmentVariables[LauncherIpcConstants.VersionEnvVar] = versionInfo.Version;
        processStartInfo.EnvironmentVariables[LauncherIpcConstants.CodenameEnvVar] = versionInfo.Codename;

        var hostProcess = Process.Start(processStartInfo);
        Console.WriteLine(
            $"[LauncherFlowCoordinator] Host launch requested. Path='{hostPath}'; WorkingDir='{hostWorkingDir}'; " +
            $"Pid={(hostProcess is null ? -1 : hostProcess.Id)}; Args='{processStartInfo.Arguments}'.");
        return (new LauncherResult
        {
            Success = true,
            Stage = "launchHost",
            Code = "ok",
            Message = "Host launched."
        }, hostProcess);
    }

    /// <summary>
    /// 显示找不到主程序的错误窗口
    /// </summary>
    private async Task<(ErrorWindowResult Result, string? CustomPath)> ShowHostNotFoundErrorAsync()
    {
        ErrorWindow? errorWindow = null;
        
        // 在 UI 线程创建并显示错误窗口
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                errorWindow = new ErrorWindow();
                errorWindow.SetErrorMessage("找不到阑山桌面应用程序。");
                errorWindow.Show();
                Console.WriteLine("[LauncherFlowCoordinator] ErrorWindow shown for host not found");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LauncherFlowCoordinator] Failed to show ErrorWindow: {ex.Message}");
            }
        });
        
        if (errorWindow is null)
        {
            Console.Error.WriteLine("[LauncherFlowCoordinator] ErrorWindow is null, cannot wait for choice");
            return (ErrorWindowResult.Exit, null);
        }
        
        // 等待用户选择
        ErrorWindowResult result;
        string? customPath;
        
        try
        {
            result = await errorWindow.WaitForChoiceAsync();
            customPath = errorWindow.GetCustomHostPath();
            Console.WriteLine($"[LauncherFlowCoordinator] ErrorWindow result: {result}, customPath: {customPath != null}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LauncherFlowCoordinator] Error waiting for choice: {ex.Message}");
            result = ErrorWindowResult.Exit;
            customPath = null;
        }
        
        // 安全关闭错误窗口
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (errorWindow.IsVisible && errorWindow.IsLoaded)
                {
                    errorWindow.Close();
                    Console.WriteLine("[LauncherFlowCoordinator] ErrorWindow closed successfully");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LauncherFlowCoordinator] Error closing ErrorWindow: {ex.Message}");
            }
        });
        
        return (result, customPath);
    }

    /// <summary>
    /// 显示迁移提示窗口
    /// </summary>
    private async Task<MigrationResult> ShowMigrationPromptAsync(LegacyVersionInfo legacyInfo)
    {
        MigrationPromptWindow? migrationWindow = null;

        // 在 UI 线程创建并显示迁移提示窗口
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                migrationWindow = new MigrationPromptWindow();
                migrationWindow.SetLegacyInfo(legacyInfo);
                migrationWindow.Show();
                Console.WriteLine("[LauncherFlowCoordinator] MigrationPromptWindow shown");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LauncherFlowCoordinator] Failed to show MigrationPromptWindow: {ex.Message}");
            }
        });

        if (migrationWindow is null)
        {
            Console.Error.WriteLine("[LauncherFlowCoordinator] MigrationPromptWindow is null, skipping migration prompt");
            return MigrationResult.Skipped;
        }

        // 等待用户选择
        MigrationResult result;

        try
        {
            result = await migrationWindow.WaitForChoiceAsync();
            Console.WriteLine($"[LauncherFlowCoordinator] MigrationPromptWindow result: {result}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LauncherFlowCoordinator] Error waiting for migration choice: {ex.Message}");
            result = MigrationResult.Skipped;
        }

        // 安全关闭窗口
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (migrationWindow.IsVisible && migrationWindow.IsLoaded)
                {
                    migrationWindow.Close();
                    Console.WriteLine("[LauncherFlowCoordinator] MigrationPromptWindow closed successfully");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LauncherFlowCoordinator] Error closing MigrationPromptWindow: {ex.Message}");
            }
        });

        return result;
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

    private sealed class WelcomeOobeStep : IOobeStep
    {
        private readonly OobeStateService _stateService;

        public WelcomeOobeStep(OobeStateService stateService)
        {
            _stateService = stateService;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            OobeWindow? window = null;
            
            try
            {
                window = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        var oobeWindow = new OobeWindow();
                        oobeWindow.Show();
                        Console.WriteLine("[WelcomeOobeStep] OOBE window shown");
                        return oobeWindow;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WelcomeOobeStep] Failed to show OOBE window: {ex.Message}");
                        return null;
                    }
                });

                if (window is null)
                {
                    Console.Error.WriteLine("[WelcomeOobeStep] OOBE window is null, skipping OOBE");
                    _stateService.MarkCompleted();
                    return;
                }

                using var _ = cancellationToken.Register(() =>
                {
                    try
                    {
                        if (window.IsVisible && window.IsLoaded)
                        {
                            window.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WelcomeOobeStep] Error closing OOBE window on cancel: {ex.Message}");
                    }
                });
                
                await window.WaitForEnterAsync().ConfigureAwait(false);
                Console.WriteLine("[WelcomeOobeStep] OOBE completed by user");
                _stateService.MarkCompleted();
            }
            finally
            {
                if (window is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try
                        {
                            if (window.IsVisible && window.IsLoaded)
                            {
                                window.Close();
                                Console.WriteLine("[WelcomeOobeStep] OOBE window closed in finally");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[WelcomeOobeStep] Error closing OOBE window in finally: {ex.Message}");
                        }
                    });
                }
            }
        }
    }
}
