using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher;

public partial class App : Application
{
    public override void Initialize()
    {
        // 初始化日志记录器
        Logger.Initialize();
        Logger.Info("Launcher starting...");
        
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var context = LauncherRuntimeContext.Current;

            // 调试模式：显示开发调试窗口
            if (context.IsDebugMode)
            {
                var devDebugWindow = new DevDebugWindow();
                devDebugWindow.Show();
                
                // 调试模式下不自动启动正常流程，由开发者通过调试窗口控制
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // 处理各界面的预览命令
            if (HandlePreviewCommand(context, desktop))
            {
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // apply-update 模式：显示 UpdateWindow，执行增量更新 + 插件升级
            if (string.Equals(context.Command, "apply-update", StringComparison.OrdinalIgnoreCase))
            {
                // 先显示窗口，再启动后台任务
                var updateWindow = new UpdateWindow();
                updateWindow.Show();
                _ = RunApplyUpdateWithWindowAsync(desktop, context, updateWindow);
            }
            else
            {
                // 先显示 Splash 窗口，确保应用程序不会立即退出
                var splashWindow = new SplashWindow();
                splashWindow.Show();
                
                // 在 try-catch 块中实例化所有服务，确保任何异常都能被捕获
                _ = RunCoordinatorWithSplashAsync(desktop, context, splashWindow);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 处理界面预览命令
    /// </summary>
    private bool HandlePreviewCommand(CommandContext context, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var command = context.Command.ToLowerInvariant();
        
        switch (command)
        {
            case "preview-splash":
                Console.WriteLine("[Launcher] Preview mode: SplashWindow");
                var splashWindow = new SplashWindow();
                splashWindow.SetDebugMode(true);
                splashWindow.Show();
                _ = SimulateSplashPreviewAsync(desktop, splashWindow);
                return true;
                
            case "preview-error":
                Console.WriteLine("[Launcher] Preview mode: ErrorWindow");
                var errorWindow = new ErrorWindow();
                errorWindow.SetErrorMessage("[预览模式] 这是一个错误页面预览。\n\n用于查看错误页面的样式和布局。");
                errorWindow.Show();
                _ = WaitForWindowCloseAsync(desktop, errorWindow);
                return true;
                
            case "preview-update":
                Console.WriteLine("[Launcher] Preview mode: UpdateWindow");
                var updateWindow = new UpdateWindow();
                updateWindow.SetDebugMode(true);
                updateWindow.Show();
                _ = SimulateUpdatePreviewAsync(desktop, updateWindow);
                return true;
                
            case "preview-oobe":
                Console.WriteLine("[Launcher] Preview mode: OobeWindow");
                var oobeWindow = new OobeWindow();
                oobeWindow.Show();
                _ = SimulateOobePreviewAsync(desktop, oobeWindow);
                return true;
                
            case "preview-debug":
                Console.WriteLine("[Launcher] Preview mode: DevDebugWindow");
                var devDebugWindow = new DevDebugWindow();
                devDebugWindow.Show();
                return true;
                
            default:
                return false;
        }
    }

    /// <summary>
    /// 模拟 Splash 窗口预览
    /// </summary>
    private async Task SimulateSplashPreviewAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow window)
    {
        var stages = new[] { "initializing", "update", "plugins", "launch", "ready" };
        var messages = new[] { "初始化...", "检查更新...", "检查插件...", "正在启动...", "就绪" };
        var reporter = (ISplashStageReporter)window;
        
        for (int i = 0; i < stages.Length; i++)
        {
            reporter.Report(stages[i], messages[i]);
            await Task.Delay(800);
        }
        
        // 等待5秒后自动关闭
        await Task.Delay(5000);
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
    }

    /// <summary>
    /// 模拟 Update 窗口预览
    /// </summary>
    private async Task SimulateUpdatePreviewAsync(IClassicDesktopStyleApplicationLifetime desktop, UpdateWindow window)
    {
        var stages = new[] { "verify", "extract", "apply", "plugins", "cleanup" };
        
        for (int i = 0; i < stages.Length; i++)
        {
            window.Report(stages[i], $"正在{GetStageName(stages[i])}...", (i + 1) * 20);
            await Task.Delay(600);
        }
        
        window.ReportComplete(true, null);
        
        // 等待3秒后自动关闭
        await Task.Delay(3000);
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
        
        string GetStageName(string stage) => stage switch
        {
            "verify" => "验证",
            "extract" => "解压",
            "apply" => "应用",
            "plugins" => "升级插件",
            "cleanup" => "清理",
            _ => stage
        };
    }

    /// <summary>
    /// 模拟 OOBE 窗口预览
    /// </summary>
    private async Task SimulateOobePreviewAsync(IClassicDesktopStyleApplicationLifetime desktop, OobeWindow window)
    {
        try
        {
            // 等待用户点击开始按钮
            await window.WaitForEnterAsync();
            Console.WriteLine("[Launcher] OOBE preview completed by user");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Launcher] OOBE preview error: {ex.Message}");
        }
        
        // 用户点击后关闭应用程序
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
    }

    /// <summary>
    /// 等待窗口关闭
    /// </summary>
    private async Task WaitForWindowCloseAsync(IClassicDesktopStyleApplicationLifetime desktop, Window window)
    {
        var tcs = new TaskCompletionSource();
        window.Closed += (s, e) => tcs.TrySetResult();
        await tcs.Task;
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
    }
    
    private static async Task RunCoordinatorWithSplashAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        CommandContext context,
        SplashWindow splashWindow)
    {
        LauncherResult result;
        ErrorWindow? errorWindow = null;
        LauncherFlowCoordinator? coordinator = null;
        
        try
        {
            // 在 try-catch 块中实例化所有服务，确保异常被捕获
            var appRoot = Commands.ResolveAppRoot(context);
            var deploymentLocator = new DeploymentLocator(appRoot);
            
            // TODO: 从配置读取 GitHub 仓库信息
            var updateCheckService = new UpdateCheckService("ClassIsland", "LanMountainDesktop");
            
            coordinator = new LauncherFlowCoordinator(
                context,
                deploymentLocator,
                new OobeStateService(appRoot),
                new UpdateEngineService(deploymentLocator),
                updateCheckService,
                new PluginInstallerService());

            result = await coordinator.RunAsync(splashWindow).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 捕获异常并显示错误窗口
            result = new LauncherResult
            {
                Success = false,
                Stage = "launch",
                Code = "exception",
                Message = $"启动器发生错误: {ex.Message}",
                ErrorMessage = ex.ToString()
            };
            
            Console.Error.WriteLine($"[Launcher] Exception caught: {ex}");
            
            // 在 UI 线程显示错误窗口 - 使用更健壮的方式
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        // 安全关闭 Splash 窗口
                        if (splashWindow.IsVisible && splashWindow.IsLoaded)
                        {
                            splashWindow.Close();
                        }
                    }
                    catch (Exception closeEx)
                    {
                        Console.Error.WriteLine($"[Launcher] Error closing splash window: {closeEx.Message}");
                    }
                    
                    // 创建并显示错误窗口
                    try
                    {
                        errorWindow = new ErrorWindow();
                        errorWindow.SetErrorMessage($"启动器发生错误:\n{ex.Message}\n\n请检查应用安装是否完整，或尝试重新安装。");
                        errorWindow.Show();
                        Console.WriteLine("[Launcher] ErrorWindow shown successfully");
                    }
                    catch (Exception windowEx)
                    {
                        Console.Error.WriteLine($"[Launcher] Failed to show ErrorWindow: {windowEx.Message}");
                    }
                });
                
                // 如果错误窗口成功显示，等待它关闭
                if (errorWindow != null)
                {
                    try
                    {
                        // 等待用户选择或窗口关闭
                        var errorResult = await errorWindow.WaitForChoiceAsync();
                        Console.WriteLine($"[Launcher] ErrorWindow result: {errorResult}");
                    }
                    catch (Exception waitEx)
                    {
                        Console.Error.WriteLine($"[Launcher] Error waiting for ErrorWindow: {waitEx.Message}");
                        // 如果等待失败，至少给用户5秒时间看到错误信息
                        await Task.Delay(5000);
                    }
                }
                else
                {
                    // 错误窗口未能显示，等待5秒让用户看到控制台输出
                    await Task.Delay(5000);
                }
            }
            catch (Exception uiEx)
            {
                // 最后的兜底：记录到控制台
                Console.Error.WriteLine($"[Launcher] Critical error in UI thread: {uiEx.Message}");
                await Task.Delay(3000);
            }
        }
        
        await Commands.WriteResultIfNeededAsync(LauncherRuntimeContext.Current.GetOption("result"), result).ConfigureAwait(false);
        Environment.ExitCode = result.Success ? 0 : 1;
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(Environment.ExitCode), DispatcherPriority.Background);
    }

    /// <summary>
    /// apply-update 模式：执行增量更新和插件升级，完成后自动退出
    /// </summary>
    private static async Task RunApplyUpdateWithWindowAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        CommandContext context,
        UpdateWindow window)
    {
        var appRoot = Commands.ResolveAppRoot(context);
        var deploymentLocator = new DeploymentLocator(appRoot);
        var updateEngine = new UpdateEngineService(deploymentLocator);
        var pluginInstaller = new PluginInstallerService();
        var pluginUpgrades = new PluginUpgradeQueueService(pluginInstaller);

        var success = true;
        string? errorMessage = null;

        try
        {
            // 1. 应用增量更新
            await Dispatcher.UIThread.InvokeAsync(() => window.Report("verify", "正在验证更新...", 10));
            var updateResult = await updateEngine.ApplyPendingUpdateAsync().ConfigureAwait(false);
            if (!updateResult.Success)
            {
                success = false;
                errorMessage = updateResult.Message;
            }

            // 2. 应用待处理的插件升级
            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() => window.Report("plugins", "正在升级插件...", 60));
                var pluginsDir = context.GetOption("plugins-dir")
                                 ?? Path.Combine(appRoot, "plugins");
                var queueResult = pluginUpgrades.ApplyPendingUpgrades(pluginsDir);
                if (!queueResult.Success && queueResult.Code != "noop")
                {
                    // 插件升级失败不阻断整体流程，仅记录到控制台
                    Console.Error.WriteLine($"Plugin upgrade had failures: {queueResult.Message}");
                }
            }

            // 3. 清理旧版本，保留至少3个版本以支持回滚
            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() => window.Report("cleanup", "正在清理...", 90));
                deploymentLocator.CleanupOldDeployments(minVersionsToKeep: 3);
            }
        }
        catch (Exception ex)
        {
            success = false;
            errorMessage = ex.Message;
        }

        // 显示完成状态，短暂停留后关闭
        await Dispatcher.UIThread.InvokeAsync(() => window.ReportComplete(success, errorMessage));

        if (success)
        {
            // 成功：停留 1.5 秒让用户看到"更新完成"
            await Task.Delay(1500);
        }
        else
        {
            // 失败：停留 5 秒让用户看到错误信息
            await Task.Delay(5000);
        }

        await Commands.WriteResultIfNeededAsync(context.GetOption("result"), new LauncherResult
        {
            Success = success,
            Stage = "apply-update",
            Code = success ? "ok" : "failed",
            Message = success ? "Update applied successfully." : (errorMessage ?? "Unknown error")
        }).ConfigureAwait(false);

        Environment.ExitCode = success ? 0 : 1;
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(Environment.ExitCode), DispatcherPriority.Background);
    }


}
