using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Launcher.ViewModels;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher.Views;

/// <summary>
/// 开发调试窗口
/// </summary>
public partial class DevDebugWindow : Window
{
    private readonly DevDebugWindowViewModel _viewModel;

    public DevDebugWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _viewModel = new DevDebugWindowViewModel();
        DataContext = _viewModel;

        // 订阅事件
        _viewModel.OpenSplashRequested += OnOpenSplashRequested;
        _viewModel.OpenErrorRequested += OnOpenErrorRequested;
        _viewModel.OpenUpdateRequested += OnOpenUpdateRequested;
        _viewModel.OpenOobeRequested += OnOpenOobeRequested;
        _viewModel.OpenDataLocationRequested += OnOpenDataLocationRequested;
        _viewModel.CloseRequested += OnCloseRequested;
    }

    /// <summary>
    /// 打开启动画面
    /// </summary>
    private void OnOpenSplashRequested(object? sender, SplashOpenEventArgs e)
    {
        var splashWindow = new SplashWindow();
        
        if (!e.IsFunctional)
        {
            // 查看模式：显示模拟内容
            splashWindow.SetDebugMode(true);
        }
        
        splashWindow.Show();
        
        if (e.IsFunctional)
        {
            // 功能模式：模拟正常启动流程
            _ = SimulateSplashProgress(splashWindow);
        }
    }

    /// <summary>
    /// 打开错误页面
    /// </summary>
    private void OnOpenErrorRequested(object? sender, ErrorOpenEventArgs e)
    {
        var errorWindow = new ErrorWindow();
        
        if (!e.IsFunctional)
        {
            // 查看模式：显示模拟错误
            errorWindow.SetDebugMode(true);
            errorWindow.SetErrorMessage("[调试模式] 这是一个模拟的错误消息，用于查看错误页面的样式和布局。");
        }
        else
        {
            // 功能模式：显示真实错误
            errorWindow.SetErrorMessage("找不到阑山桌面应用程序。\n\n请检查应用安装是否完整。");
        }
        
        errorWindow.Show();
    }

    /// <summary>
    /// 打开更新页面
    /// </summary>
    private void OnOpenUpdateRequested(object? sender, UpdateOpenEventArgs e)
    {
        var updateWindow = new UpdateWindow();
        
        if (!e.IsFunctional)
        {
            // 查看模式：显示模拟更新
            updateWindow.SetDebugMode(true);
        }
        
        updateWindow.Show();
        
        if (e.IsFunctional)
        {
            // 功能模式：模拟更新进度
            _ = SimulateUpdateProgress(updateWindow);
        }
    }

    /// <summary>
    /// 打开OOBE页面
    /// </summary>
    private void OnOpenOobeRequested(object? sender, OobeOpenEventArgs e)
    {
        var oobeWindow = new OobeWindow();
        
        if (!e.IsFunctional)
        {
            // 查看模式：显示调试标记（通过标题）
            oobeWindow.Title = "[调试模式] 欢迎使用阑山桌面";
        }
        
        oobeWindow.Show();
        
        if (e.IsFunctional)
        {
            // 功能模式：等待用户点击后自动关闭
            _ = SimulateOobeProgress(oobeWindow);
        }
    }

    /// <summary>
    /// 模拟OOBE流程
    /// </summary>
    private async Task SimulateOobeProgress(OobeWindow oobeWindow)
    {
        try
        {
            // 等待用户点击开始按钮
            await oobeWindow.WaitForEnterAsync();
            
            // 用户点击后，窗口会自动关闭（通过OobeWindow内部的动画和关闭逻辑）
            Console.WriteLine("[DevDebugWindow] OOBE completed by user");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DevDebugWindow] Error during OOBE simulation: {ex.Message}");
        }
    }

    /// <summary>
    /// 打开数据位置选择页面
    /// </summary>
    private void OnOpenDataLocationRequested(object? sender, DataLocationOpenEventArgs e)
    {
        var appRoot = AppDomain.CurrentDomain.BaseDirectory;
        var resolver = new DataLocationResolver(appRoot);
        var window = new DataLocationPromptWindow(resolver);
        window.Show();
    }

    /// <summary>
    /// 关闭窗口
    /// </summary>
    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 模拟启动画面进度
    /// </summary>
    private async Task SimulateSplashProgress(SplashWindow splashWindow)
    {
        var stages = new[] { "初始化", "检查更新", "加载组件", "启动应用" };
        var reporter = (ISplashStageReporter)splashWindow;
        
        for (int i = 0; i < stages.Length; i++)
        {
            reporter.ReportStage(stages[i], (i + 1) * 25);
            await Task.Delay(500);
        }
        
        // 3秒后关闭
        await Task.Delay(3000);
        splashWindow.Close();
    }

    /// <summary>
    /// 模拟更新进度
    /// </summary>
    private async Task SimulateUpdateProgress(UpdateWindow updateWindow)
    {
        var stages = new[] { "下载", "验证", "安装", "清理" };
        
        foreach (var stage in stages)
        {
            updateWindow.Report(stage, $"正在{stage}...", Array.IndexOf(stages, stage) * 25 + 10);
            await Task.Delay(800);
        }
        
        updateWindow.ReportComplete(true, null);
        
        // 2秒后关闭
        await Task.Delay(2000);
        updateWindow.Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        // 取消订阅事件
        _viewModel.OpenSplashRequested -= OnOpenSplashRequested;
        _viewModel.OpenErrorRequested -= OnOpenErrorRequested;
        _viewModel.OpenUpdateRequested -= OnOpenUpdateRequested;
        _viewModel.OpenOobeRequested -= OnOpenOobeRequested;
        _viewModel.CloseRequested -= OnCloseRequested;
        
        base.OnClosed(e);
    }
}
