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

    public async Task<LauncherResult> RunAsync()
    {
        try
        {
            // 清理待删除的旧版本
            _deploymentLocator.CleanupDestroyedDeployments();

            // 显示 Splash 窗口
            var splashWindow = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new SplashWindow();
                window.Show();
                return window;
            });

            var reporter = (ISplashStageReporter)splashWindow;
            
            // 启动 IPC 服务端监听主程序进度
            using var ipcServer = new LauncherIpcServer(msg =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    reporter.Report(msg.Stage.ToString().ToLower(), msg.Message ?? "");
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
                var hostResult = await LaunchHostWithIpcAsync();
                if (!hostResult.Success)
                {
                    return hostResult;
                }

                // 等待主程序就绪或超时
                await Task.Delay(TimeSpan.FromSeconds(30));

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
                await Dispatcher.UIThread.InvokeAsync(() => splashWindow.Close());
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
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<LauncherResult> LaunchHostWithIpcAsync(string? customHostPath = null)
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
                    return await LaunchHostWithIpcAsync(selectedPath);
                }
                return await LaunchHostWithIpcAsync();
            }
            
            // 用户选择退出
            return new LauncherResult
            {
                Success = false,
                Stage = "launchHost",
                Code = "host_not_found",
                Message = "LanMountainDesktop host executable not found."
            };
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            EnsureExecutable(hostPath);
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(hostPath) ?? _deploymentLocator.GetAppRoot()
        };

        // 传递环境变量供 IPC 使用
        processStartInfo.EnvironmentVariables[LauncherIpcConstants.LauncherPidEnvVar] = 
            Environment.ProcessId.ToString();
        processStartInfo.EnvironmentVariables[LauncherIpcConstants.PackageRootEnvVar] = 
            _deploymentLocator.GetAppRoot();

        Process.Start(processStartInfo);
        return new LauncherResult
        {
            Success = true,
            Stage = "launchHost",
            Code = "ok",
            Message = "Host launched."
        };
    }

    /// <summary>
    /// 显示找不到主程序的错误窗口
    /// </summary>
    private async Task<(ErrorWindowResult Result, string? CustomPath)> ShowHostNotFoundErrorAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var errorWindow = new ErrorWindow();
            errorWindow.SetErrorMessage("找不到阑山桌面应用程序。");
            errorWindow.Show();
            
            var result = await errorWindow.WaitForChoiceAsync();
            var customPath = errorWindow.GetCustomHostPath();
            
            await Dispatcher.UIThread.InvokeAsync(() => errorWindow.Close());
            
            return (result, customPath);
        });
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
            var window = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var oobeWindow = new OobeWindow();
                oobeWindow.Show();
                return oobeWindow;
            });

            try
            {
                using var _ = cancellationToken.Register(() => window.Close());
                await window.WaitForEnterAsync().ConfigureAwait(false);
                _stateService.MarkCompleted();
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => window.Close());
            }
        }
    }
}
