using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Win32;
using LanDesktopPLONDS.Installer.Services;

namespace LanDesktopPLONDS.Installer;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        InstallerStartupDiagnostics.Initialize();

        // 解析命令行参数
        var uninstallMode = false;
        var uninstallSilent = false;
        string? uninstallPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                uninstallMode = true;
                // 下一个参数是可选的 installPath
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    uninstallPath = args[i + 1];
                    i++;
                }
            }
            else if (args[i].Equals("--uninstall-silent", StringComparison.OrdinalIgnoreCase))
            {
                uninstallMode = true;
                uninstallSilent = true;
                // 下一个参数是可选的 installPath
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    uninstallPath = args[i + 1];
                    i++;
                }
            }
        }

        // 卸载模式
        if (uninstallMode)
        {
            RunUninstall(uninstallPath, uninstallSilent);
            return;
        }

        // 单实例检查
        using var singleInstance = new InstallerSingleInstance();
        if (!singleInstance.TryAcquire())
        {
            Console.Error.WriteLine("安装程序已在运行，无法启动第二个实例。");
            Environment.Exit(2);
            return;
        }

        try
        {
            InstallerStartupDiagnostics.Log("Starting Avalonia desktop lifetime.");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            InstallerStartupDiagnostics.ReportFatal("The installer failed to start.", ex);
        }
    }

    /// <summary>
    /// 执行卸载流程。
    /// </summary>
    private static void RunUninstall(string? installPath, bool silent)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            Console.Error.WriteLine("卸载需要指定安装路径参数。");
            Environment.Exit(1);
            return;
        }

        try
        {
            // 非静默模式：显示确认窗口
            if (!silent)
            {
                var confirmed = ShowUninstallConfirmation(installPath);
                if (!confirmed)
                {
                    Console.WriteLine("用户取消了卸载操作。");
                    Environment.Exit(0);
                    return;
                }
            }

            var service = new UninstallService(installPath, silent);
            var success = service.Execute();
            Environment.Exit(success ? 0 : 1);
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"卸载失败：{ex.Message}");
            Environment.Exit(1);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"卸载失败：{ex.Message}");
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"卸载过程中发生意外错误：{ex.Message}");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// 显示卸载确认窗口（非静默模式）。
    /// 返回 true 表示用户确认卸载。
    /// </summary>
    private static bool ShowUninstallConfirmation(string installPath)
    {
        var confirmed = false;
        var resetEvent = new ManualResetEventSlim(false);

        // 在新线程启动 Avalonia 卸载确认窗口
        var thread = new Thread(() =>
        {
            try
            {
                var appBuilder = AppBuilder.Configure<UninstallConfirmApp>()
                    .UsePlatformDetect()
                    .With(new Win32PlatformOptions
                    {
                        RenderingMode = [Win32RenderingMode.Software],
                        CompositionMode = [Win32CompositionMode.RedirectionSurface]
                    });

                var app = new UninstallConfirmApp();
                app.ConfirmAction += result =>
                {
                    confirmed = result;
                    resetEvent.Set();
                    app.ShutdownApp();
                };
                app.InstallPath = installPath;

                appBuilder.StartWithClassicDesktopLifetime([]);
            }
            catch
            {
                resetEvent.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        resetEvent.Wait();

        return confirmed;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software],
                CompositionMode = [Win32CompositionMode.RedirectionSurface]
            });
    }
}

/// <summary>
/// 卸载确认窗口的临时 App 类。
/// </summary>
internal sealed class UninstallConfirmApp : Application
{
    private IClassicDesktopStyleApplicationLifetime? _lifetime;

    public event Action<bool>? ConfirmAction;
    public string InstallPath { get; set; } = string.Empty;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _lifetime = desktop;
            var window = new Views.UninstallConfirmWindow
            {
                DataContext = new UninstallConfirmViewModel(InstallPath, ConfirmAction!)
            };
            desktop.MainWindow = window;
            window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 关闭应用程序。
    /// </summary>
    public void ShutdownApp()
    {
        _lifetime?.Shutdown();
    }
}

/// <summary>
/// 卸载确认窗口的视图模型。
/// </summary>
internal sealed class UninstallConfirmViewModel
{
    private readonly Action<bool> _confirmAction;

    public UninstallConfirmViewModel(string installPath, Action<bool> confirmAction)
    {
        InstallPath = installPath;
        _confirmAction = confirmAction;
    }

    public string InstallPath { get; }

    public void Confirm()
    {
        _confirmAction(true);
    }

    public void Cancel()
    {
        _confirmAction(false);
    }
}
