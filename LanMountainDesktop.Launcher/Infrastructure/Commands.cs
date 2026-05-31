using System.Text;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Infrastructure;

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
        var pluginInstaller = new PluginInstallerService();
        var pluginUpgrades = new PluginUpgradeQueueService(pluginInstaller);

        LauncherResult result;
        try
        {
            result = ExecuteCore(context, pluginInstaller, pluginUpgrades);
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

    private static LauncherResult ExecuteCore(
        CommandContext context,
        PluginInstallerService pluginInstaller,
        PluginUpgradeQueueService pluginUpgrades)
    {
        switch (context.Command.ToLowerInvariant())
        {
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
        
        var appDirs = Directory.GetDirectories(baseDir, "app-*", SearchOption.TopDirectoryOnly);
        if (appDirs.Length > 0)
        {
            return baseDir;
        }
        
        var parent = Path.GetFullPath(Path.Combine(baseDir, ".."));
        var parentHost = OperatingSystem.IsWindows()
            ? Path.Combine(parent, "LanMountainDesktop.exe")
            : Path.Combine(parent, "LanMountainDesktop");
        if (File.Exists(parentHost))
        {
            return parent;
        }
        
        return baseDir;
    }
}
