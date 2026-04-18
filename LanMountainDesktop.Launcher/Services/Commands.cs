using System.Text;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Services;

internal static class Commands
{
    public static async Task<int> RunLegacyPluginInstallAsync(CommandContext context, PluginInstallerService installer)
    {
        var resultPath = context.GetOption("result");
        LauncherResult result;
        try
        {
            var source = context.GetOption("source") ?? string.Empty;
            var pluginsDir = context.GetOption("plugins-dir") ?? string.Empty;
            result = installer.InstallPackage(source, pluginsDir);
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
        var deploymentLocator = new DeploymentLocator(appRoot);
        var updateEngine = new UpdateEngineService(deploymentLocator);
        var pluginInstaller = new PluginInstallerService();
        var pluginUpgrades = new PluginUpgradeQueueService(pluginInstaller);

        LauncherResult result;
        try
        {
            result = await ExecuteCoreAsync(context, updateEngine, pluginInstaller, pluginUpgrades).ConfigureAwait(false);
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
        UpdateEngineService updateEngine,
        PluginInstallerService pluginInstaller,
        PluginUpgradeQueueService pluginUpgrades)
    {
        switch (context.Command.ToLowerInvariant())
        {
            case "update":
                return await ExecuteUpdateAsync(context, updateEngine).ConfigureAwait(false);
            case "plugin":
                return ExecutePluginCommand(context, pluginInstaller, pluginUpgrades);
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

    private static async Task<LauncherResult> ExecuteUpdateAsync(CommandContext context, UpdateEngineService updateEngine)
    {
        return context.SubCommand.ToLowerInvariant() switch
        {
            "check" => updateEngine.CheckPendingUpdate(),
            "apply" => await updateEngine.ApplyPendingUpdateAsync().ConfigureAwait(false),
            "rollback" => updateEngine.RollbackLatest(),
            "download" => await updateEngine.DownloadAsync(
                context.GetOption("manifest-url") ?? throw new InvalidOperationException("Missing --manifest-url."),
                context.GetOption("signature-url") ?? throw new InvalidOperationException("Missing --signature-url."),
                context.GetOption("archive-url") ?? throw new InvalidOperationException("Missing --archive-url."),
                CancellationToken.None).ConfigureAwait(false),
            _ => new LauncherResult
            {
                Success = false,
                Stage = "update",
                Code = "unsupported_subcommand",
                Message = $"Unsupported update sub-command '{context.SubCommand}'."
            }
        };
    }

    private static LauncherResult ExecutePluginCommand(
        CommandContext context,
        PluginInstallerService pluginInstaller,
        PluginUpgradeQueueService pluginUpgrades)
    {
        switch (context.SubCommand.ToLowerInvariant())
        {
            case "install":
            {
                var source = context.GetOption("source") ?? throw new InvalidOperationException("Missing --source.");
                var pluginsDir = context.GetOption("plugins-dir") ?? throw new InvalidOperationException("Missing --plugins-dir.");
                return pluginInstaller.InstallPackage(source, pluginsDir);
            }
            case "update":
            {
                var pluginsDir = context.GetOption("plugins-dir") ?? throw new InvalidOperationException("Missing --plugins-dir.");
                return pluginUpgrades.ApplyPendingUpgrades(pluginsDir);
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

        var baseDir = AppContext.BaseDirectory;
        
        // 发布版结构：Launcher 和 app-* 目录在同一目录
        // 检查当前目录是否有 app-* 子目录（发布版）
        var appDirs = Directory.GetDirectories(baseDir, "app-*", SearchOption.TopDirectoryOnly);
        if (appDirs.Length > 0)
        {
            // 找到 app-* 目录，说明是发布版结构
            return baseDir;
        }
        
        // 开发环境：检查父目录是否有主程序
        var parent = Path.GetFullPath(Path.Combine(baseDir, ".."));
        var parentHost = OperatingSystem.IsWindows()
            ? Path.Combine(parent, "LanMountainDesktop.exe")
            : Path.Combine(parent, "LanMountainDesktop");
        if (File.Exists(parentHost))
        {
            return parent;
        }
        
        // 默认返回 baseDir
        return baseDir;
    }
}
