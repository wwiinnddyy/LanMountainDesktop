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
        var execution = LauncherExecutionContext.Capture();
        Logger.Initialize();
        Logger.Info(
            $"Program entry. Command='{commandContext.Command}'; SubCommand='{commandContext.SubCommand}'; " +
            $"IsGuiMode={commandContext.IsGuiCommand}; IsDebugMode={commandContext.IsDebugMode}; " +
            $"LaunchSource='{commandContext.LaunchSource}'; IsElevated={execution.IsElevated}; " +
            $"UserSid='{execution.UserSid ?? string.Empty}'; " +
            $"HasResultPath={!string.IsNullOrWhiteSpace(commandContext.GetOption("result"))}; " +
            $"ExplicitAppRoot='{commandContext.ExplicitAppRoot ?? "<none>"}'.");

        try
        {
            if (commandContext.IsLegacyPluginInstall)
            {
                var installer = new PluginInstallerService();
                return await Commands.RunLegacyPluginInstallAsync(commandContext, installer).ConfigureAwait(false);
            }

            if (!commandContext.IsGuiCommand)
            {
                return await Commands.RunCliCommandAsync(commandContext).ConfigureAwait(false);
            }

            LauncherRuntimeContext.Current = commandContext;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return Environment.ExitCode;
        }
        catch (Exception ex)
        {
            Logger.Error("Launcher failed before GUI flow completed.", ex);

            var result = new LauncherResult
            {
                Success = false,
                Stage = "launcher",
                Code = "launcher_bootstrap_failed",
                Message = ex.Message,
                ErrorMessage = ex.ToString(),
                Details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["command"] = commandContext.Command,
                    ["subCommand"] = commandContext.SubCommand,
                    ["launchSource"] = commandContext.LaunchSource,
                    ["isGuiMode"] = commandContext.IsGuiCommand.ToString(),
                    ["isDebugMode"] = commandContext.IsDebugMode.ToString(),
                    ["isElevated"] = execution.IsElevated.ToString(),
                    ["userSid"] = execution.UserSid ?? string.Empty,
                    ["explicitAppRoot"] = commandContext.ExplicitAppRoot ?? string.Empty
                }
            };

            await Commands.WriteResultIfNeededAsync(commandContext.GetOption("result"), result).ConfigureAwait(false);
            return 1;
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
