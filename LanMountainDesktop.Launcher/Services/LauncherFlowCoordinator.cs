using System.Diagnostics;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services.Ipc;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class LauncherFlowCoordinator
{
    private readonly CommandContext _context;
    private readonly DeploymentLocator _deploymentLocator;
    private readonly OobeStateService _oobeStateService;
    private readonly UpdateEngineService _updateEngine;
    private readonly UpdateCheckService _updateCheckService;
    private readonly PluginInstallerService _pluginInstallerService;
    private readonly IReadOnlyList<IOobeStep> _oobeSteps;

    public LauncherFlowCoordinator(
        CommandContext context,
        DeploymentLocator deploymentLocator,
        OobeStateService oobeStateService,
        UpdateEngineService updateEngine,
        UpdateCheckService updateCheckService,
        PluginInstallerService pluginInstallerService)
    {
        _context = context;
        _deploymentLocator = deploymentLocator;
        _oobeStateService = oobeStateService;
        _updateEngine = updateEngine;
        _updateCheckService = updateCheckService;
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
            
            // 跟踪主程序是否已就绪，就绪后自动关闭 Splash 窗口
            var hostReadyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            
            // 启动 IPC 服务端监听主程序进度
            using var ipcServer = new LauncherIpcServer(msg =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        reporter.Report(msg.Stage.ToString().ToLower(), msg.Message ?? "");
                        
                        // 主程序报告就绪后，关闭 Splash 窗口
                        if (msg.Stage == StartupStage.Ready && splashWindow.IsVisible && splashWindow.IsLoaded)
                        {
                            splashWindow.Close();
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
                    // 等待主程序就绪或进程退出（取先发生者）
                    // 如果主程序在 60 秒内未报告 Ready，也关闭 Splash 窗口作为超时保护
                    var readyOrTimeout = Task.WhenAny(
                        hostReadyTcs.Task,
                        Task.Delay(TimeSpan.FromSeconds(60)));
                    
                    var processExitTask = hostProcess.WaitForExitAsync();
                    
                    // 先等待就绪/超时，然后等待进程退出
                    await readyOrTimeout;
                    
                    // 如果 Splash 窗口仍然打开（超时情况），关闭它
                    if (splashWindow.IsVisible)
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
                                Console.Error.WriteLine($"[LauncherFlowCoordinator] Error closing splash window on timeout: {ex.Message}");
                            }
                        });
                    }
                    
                    await processExitTask;
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

        var processStartInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(hostPath) ?? _deploymentLocator.GetAppRoot()
        };

        // 转发命令行参数给主程序（排除 Launcher 自己的命令和选项）
        foreach (var arg in _context.RawArgs)
        {
            // 跳过 Launcher 自己的命令和选项，只传递用户原始参数
            if (arg == _context.Command || arg == _context.SubCommand || arg.StartsWith("--"))
            {
                continue;
            }
            processStartInfo.ArgumentList.Add(arg);
        }

        // 传递环境变量供 IPC 使用
        processStartInfo.EnvironmentVariables[LauncherIpcConstants.LauncherPidEnvVar] = 
            Environment.ProcessId.ToString();
        processStartInfo.EnvironmentVariables[LauncherIpcConstants.PackageRootEnvVar] = 
            _deploymentLocator.GetAppRoot();
        
        // 传递版本信息
        var versionInfo = _deploymentLocator.GetVersionInfo();
        processStartInfo.EnvironmentVariables[LauncherIpcConstants.VersionEnvVar] = versionInfo.Version;
        processStartInfo.EnvironmentVariables[LauncherIpcConstants.CodenameEnvVar] = versionInfo.Codename;

        var hostProcess = Process.Start(processStartInfo);
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
