using System.Threading.Tasks;
using Avalonia;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Shell;
namespace LanMountainDesktop.Launcher;

public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        // 注册全局异常处理器，防止 async void / 未观察任务异常导致启动器崩溃并显示堆栈对话框
        RegisterGlobalExceptionHandlers();

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
            LauncherServiceRegistration.Initialize(commandContext);

            var appRoot = Commands.ResolveAppRoot(commandContext);
            var languageCode = LanguagePreferenceService.ResolveLanguageCode(appRoot);
            LanguagePreferenceService.ApplyLanguage(languageCode);

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

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }

    /// <summary>
    /// 注册全局异常处理器，避免 async void（如 ErrorWindow 的复制按钮）抛出未捕获异常时
    /// 触发 Windows 崩溃对话框显示堆栈跟踪，从而让用户看到无法理解的报错。
    /// </summary>
    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            try
            {
                var exception = eventArgs.ExceptionObject as Exception
                    ?? new Exception(eventArgs.ExceptionObject?.ToString() ?? "Unhandled exception.");
                Logger.Error($"Launcher unhandled exception. IsTerminating={eventArgs.IsTerminating}.", exception);
            }
            catch
            {
                // 全局处理器本身不能再抛出异常
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            try
            {
                Logger.Error("Launcher unobserved task exception.", eventArgs.Exception);
            }
            catch
            {
                // 忽略日志写入失败
            }
            eventArgs.SetObserved();
        };
    }
}
