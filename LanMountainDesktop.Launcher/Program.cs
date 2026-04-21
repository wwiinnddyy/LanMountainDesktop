using Avalonia;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;

namespace LanMountainDesktop.Launcher;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var commandContext = CommandContext.FromArgs(args);

        // 处理遗留插件安装命令
        if (commandContext.IsLegacyPluginInstall)
        {
            var installer = new PluginInstallerService();
            return await Commands.RunLegacyPluginInstallAsync(commandContext, installer).ConfigureAwait(false);
        }

        // apply-update 命令：启动 Avalonia GUI 显示更新进度窗口
        if (string.Equals(commandContext.Command, "apply-update", StringComparison.OrdinalIgnoreCase))
        {
            LauncherRuntimeContext.Current = commandContext;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return Environment.ExitCode;
        }

        // 处理其他 CLI 命令 (update, plugin, rollback 等)
        if (!string.Equals(commandContext.Command, "launch", StringComparison.OrdinalIgnoreCase))
        {
            return await Commands.RunCliCommandAsync(commandContext).ConfigureAwait(false);
        }

        // 主启动流程: OOBE -> Splash -> 版本选择 -> 启动主程序
        LauncherRuntimeContext.Current = commandContext;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return Environment.ExitCode;
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
