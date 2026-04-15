using System.Diagnostics;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class LauncherFlowCoordinator
{
    private readonly CommandContext _context;
    private readonly DeploymentLocator _deploymentLocator;
    private readonly OobeStateService _oobeStateService;
    private readonly UpdateEngineService _updateEngine;
    private readonly UpdateCheckService _updateCheckService;
    private readonly PluginInstallerService _pluginInstallerService;
    private readonly ISplashStageReporter _splashStageReporter;
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
        _splashStageReporter = new NullSplashStageReporter();
        _oobeSteps = [new WelcomeOobeStep(_oobeStateService)];
    }

    public async Task<LauncherResult> RunAsync()
    {
        try
        {
            // 清理待删除的旧版本
            _deploymentLocator.CleanupDestroyedDeployments();

            _splashStageReporter.Report("bootstrap", "bootstrap");
            if (_oobeStateService.IsFirstRun())
            {
                foreach (var step in _oobeSteps)
                {
                    await step.RunAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            var splashWindow = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new SplashWindow();
                window.Show();
                return window;
            });

            try
            {
                _splashStageReporter.Report("silentUpdate", "update");
                var updateResult = _updateEngine.ApplyPendingUpdate();
                if (!updateResult.Success)
                {
                    return updateResult;
                }

                _splashStageReporter.Report("pluginTasks", "plugins");
                var pluginsDir = _context.GetOption("plugins-dir")
                                 ?? Path.Combine(_deploymentLocator.GetAppRoot(), "plugins");
                var queueResult = new PluginUpgradeQueueService(_pluginInstallerService).ApplyPendingUpgrades(pluginsDir);
                if (!queueResult.Success)
                {
                    return queueResult;
                }

                _splashStageReporter.Report("launchHost", "launch");
                var hostResult = LaunchHost();
                if (!hostResult.Success)
                {
                    return hostResult;
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

    private LauncherResult LaunchHost()
    {
        var hostPath = _deploymentLocator.ResolveHostExecutablePath();
        if (string.IsNullOrWhiteSpace(hostPath))
        {
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

        Process.Start(processStartInfo);
        return new LauncherResult
        {
            Success = true,
            Stage = "launchHost",
            Code = "ok",
            Message = "Host launched."
        };
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

    private sealed class NullSplashStageReporter : ISplashStageReporter
    {
        public void Report(string stage, string message)
        {
            _ = stage;
            _ = message;
        }
    }
}
