using System.Text;
using LanMountainDesktop.Shared.Data;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Startup;
using LanMountainDesktop.Shared.Contracts.Deployment;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Infrastructure;

internal static class Commands
{
    public static async Task<int> RunLegacyAirAppInstallAsync(CommandContext context, AirAppInstallerService installer)
    {
        var resultPath = context.GetOption("result");
        LauncherResult result;
        try
        {
            var source = context.GetOption("source") ?? string.Empty;
            var pluginsDir = context.GetOption("plugins-dir") ?? string.Empty;
            result = installer.InstallPackage(source, pluginsDir, context.ExplicitAppRoot);
        }
        catch (Exception ex)
        {
            result = new LauncherResult
            {
                Success = false,
                Stage = "plugin.install",
                Code = "failed",
                Message = ex.Message,
                ErrorMessage = ex.Message
            };
        }

        await WriteResultIfNeededAsync(resultPath, result).ConfigureAwait(false);
        return result.Success ? 0 : 1;
    }

    public static async Task<int> RunCliCommandAsync(CommandContext context)
    {
        var appRoot = ResolveAppRoot(context);
        _ = new DeploymentLocator(appRoot);
        var pluginInstaller = new AirAppInstallerService();
        var pluginUpgrades = new AirAppUpgradeQueueService(pluginInstaller);

        LauncherResult result;
        try
        {
            result = await ExecuteCoreAsync(context, pluginInstaller, pluginUpgrades).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new LauncherResult
            {
                Success = false,
                Stage = "command",
                Code = "exception",
                Message = ex.Message,
                ErrorMessage = ex.Message
            };
        }

        await WriteResultIfNeededAsync(context.GetOption("result"), result).ConfigureAwait(false);
        return result.Success ? 0 : 1;
    }

    private static async Task<LauncherResult> ExecuteCoreAsync(
        CommandContext context,
        AirAppInstallerService pluginInstaller,
        AirAppUpgradeQueueService pluginUpgrades)
    {
        switch (context.Command.ToLowerInvariant())
        {
            case "plugin":
                return ExecuteAirAppCommand(context, pluginInstaller, pluginUpgrades);
            case "desktop":
                return await ExecuteDesktopCommandAsync(context).ConfigureAwait(false);
            default:
                return new LauncherResult
                {
                    Success = false,
                    Stage = "command",
                    Code = "unsupported_command",
                    Message = $"Unsupported command '{context.Command}'."
                };
        }
    }

    private static LauncherResult ExecuteAirAppCommand(
        CommandContext context,
        AirAppInstallerService pluginInstaller,
        AirAppUpgradeQueueService pluginUpgrades)
    {
        switch (context.SubCommand.ToLowerInvariant())
        {
            case "install":
            {
                var source = context.GetOption("source") ?? throw new InvalidOperationException("Missing --source.");
                var pluginsDir = context.GetOption("plugins-dir") ?? throw new InvalidOperationException("Missing --plugins-dir.");
                return pluginInstaller.InstallPackage(source, pluginsDir, context.ExplicitAppRoot);
            }
            case "update":
            {
                var pluginsDir = context.GetOption("plugins-dir") ?? throw new InvalidOperationException("Missing --plugins-dir.");
                return pluginUpgrades.ApplyPendingUpgrades(pluginsDir, context.ExplicitAppRoot);
            }
            default:
                return new LauncherResult
                {
                    Success = false,
                    Stage = "plugin",
                    Code = "unsupported_subcommand",
                    Message = $"Unsupported plugin sub-command '{context.SubCommand}'."
                };
        }
    }

    private static async Task<LauncherResult> ExecuteDesktopCommandAsync(CommandContext context)
    {
        switch (context.SubCommand.ToLowerInvariant())
        {
            case "exit":
                return await ExecuteDesktopExitAsync().ConfigureAwait(false);
            default:
                return new LauncherResult
                {
                    Success = false,
                    Stage = "desktop",
                    Code = "unsupported_subcommand",
                    Message = $"Unsupported desktop sub-command '{context.SubCommand}'."
                };
        }
    }

    /// <summary>
    /// 退出这条通道一直存在（宿主把 IPublicShellControlService.ExitAsync 注册在公共 IPC 上），
    /// 这里只把动词接上、不新建第二条退出路径：连接走 PublicIpcConnection 那一家，
    /// 落点仍是宿主自己的 HostApplicationLifecycleService。
    /// 宿主那边是先受理（TrySubmitShutdown 立刻返回）再生效，所以 true = 已受理，不等于进程已经退完。
    /// </summary>
    private static async Task<LauncherResult> ExecuteDesktopExitAsync()
    {
        var timeout = TimeSpan.FromSeconds(5);
        using var ipcClient = new LanMountainDesktopIpcClient();
        if (!await PublicIpcConnection.TryConnectAsync(ipcClient, timeout).ConfigureAwait(false))
        {
            return new LauncherResult
            {
                Success = false,
                Stage = "desktop.exit",
                Code = "host_not_connected",
                Message = "No running desktop host is reachable over the public IPC pipe."
            };
        }

        var shellProxy = ipcClient.CreateProxy<IPublicShellControlService>();
        var exitTask = shellProxy.ExitAsync();
        var completedTask = await Task.WhenAny(exitTask, Task.Delay(timeout)).ConfigureAwait(false);
        if (completedTask != exitTask)
        {
            return new LauncherResult
            {
                Success = false,
                Stage = "desktop.exit",
                Code = "host_not_responding",
                Message = $"The running desktop host did not answer the exit request within {timeout.TotalSeconds:0} seconds."
            };
        }

        var accepted = await exitTask.ConfigureAwait(false);
        return new LauncherResult
        {
            Success = accepted,
            Stage = "desktop.exit",
            Code = accepted ? "exit_requested" : "exit_refused",
            Message = accepted
                ? "The running desktop host accepted the exit request."
                : "The running desktop host refused the exit request (desktop lifetime unavailable, or shutdown already in progress)."
        };
    }

    public static async Task WriteResultIfNeededAsync(string? resultPath, LauncherResult result)
    {
        if (string.IsNullOrWhiteSpace(resultPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(resultPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(result, AppJsonContext.Default.LauncherResult);
        await File.WriteAllTextAsync(fullPath, json, Encoding.UTF8).ConfigureAwait(false);
    }

    public static string ResolveAppRoot(CommandContext context)
    {
        var configured = context.GetOption("app-root");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var launcherDir = Path.GetDirectoryName(Environment.ProcessPath);
        var baseDir = Path.GetFullPath(!string.IsNullOrWhiteSpace(launcherDir)
            ? launcherDir
            : AppContext.BaseDirectory);
        
        var searchPattern = DeploymentLayout.DeploymentDirectoryPrefix + "*";
        var appDirs = Directory.GetDirectories(baseDir, searchPattern, SearchOption.TopDirectoryOnly);
        if (appDirs.Length > 0)
        {
            return baseDir;
        }
        
        var parent = Path.GetFullPath(Path.Combine(baseDir, ".."));
        var parentHost = Path.Combine(parent, DeploymentLayout.GetHostExecutableName());
        if (File.Exists(parentHost))
        {
            return parent;
        }
        
        return baseDir;
    }
}
