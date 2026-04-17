using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Services;

namespace LanMountainDesktop.Launcher.Views;

/// <summary>
/// 启动画面窗口 - 简洁设计
/// </summary>
public partial class SplashWindow : Window, ISplashStageReporter
{
    private int _versionTextClickCount = 0;
    private const int DebugModeClickThreshold = 5;
    private bool _isDebugModeOpened = false;

    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
        
        // 延迟到窗口加载完成后再绑定事件
        this.Loaded += OnWindowLoaded;
    }

    /// <summary>
    /// 窗口加载完成事件
    /// </summary>
    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("[SplashWindow] Window loaded, binding events...");
        
        // 绑定版本文本点击事件（隐藏功能：点击5次打开开发者界面）
        var versionTextBorder = this.FindControl<Border>("VersionTextBorder");
        if (versionTextBorder is not null)
        {
            versionTextBorder.PointerPressed += OnVersionTextClick;
            Console.WriteLine("[SplashWindow] VersionTextBorder click event bound");
        }
        else
        {
            Console.Error.WriteLine("[SplashWindow] Failed to find VersionTextBorder!");
        }
    }

    /// <summary>
    /// 版本文本点击事件 - 连续点击5次打开开发者界面（隐藏功能）
    /// </summary>
    private void OnVersionTextClick(object? sender, PointerPressedEventArgs e)
    {
        if (_isDebugModeOpened) return;
        
        _versionTextClickCount++;
        Console.WriteLine($"[SplashWindow] Version text clicked {_versionTextClickCount}/{DebugModeClickThreshold}");

        if (_versionTextClickCount >= DebugModeClickThreshold)
        {
            OpenDebugWindow();
        }
    }

    /// <summary>
    /// 打开开发者调试窗口
    /// </summary>
    private async void OpenDebugWindow()
    {
        _isDebugModeOpened = true;
        Console.WriteLine("[SplashWindow] Opening debug window...");

        try
        {
            // 加载保存的状态
            var devModeEnabled = ErrorWindow.CheckDevModeEnabled();
            var customHostPath = ErrorWindow.GetSavedCustomHostPath();

            var debugWindow = new ErrorDebugWindow(devModeEnabled, customHostPath)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            // 订阅窗口关闭事件以保存状态
            debugWindow.Closed += (s, e) =>
            {
                Console.WriteLine("[SplashWindow] Debug window closed");
                _isDebugModeOpened = false;
                _versionTextClickCount = 0;
            };

            await debugWindow.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SplashWindow] Error opening debug window: {ex.Message}");
            _isDebugModeOpened = false;
            _versionTextClickCount = 0;
        }
    }

    /// <summary>
    /// 更新进度和状态
    /// </summary>
    public void Report(string stage, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            var progressIndicator = this.FindControl<ProgressBar>("ProgressIndicator");
            
            if (statusText is null || progressIndicator is null)
            {
                Console.Error.WriteLine($"[SplashWindow] Controls not found: StatusText={statusText != null}, ProgressIndicator={progressIndicator != null}");
                return;
            }

            // 更新状态文本
            statusText.Text = message;

            // 根据阶段更新进度
            var progress = ResolveProgress(stage);
            if (progress > 0)
            {
                progressIndicator.IsIndeterminate = false;
                progressIndicator.Value = progress;
            }
            else
            {
                progressIndicator.IsIndeterminate = true;
            }
        });
    }

    /// <summary>
    /// 更新进度（0-100）
    /// </summary>
    public void UpdateProgress(int percent, string? message = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            var progressIndicator = this.FindControl<ProgressBar>("ProgressIndicator");
            
            if (statusText is null || progressIndicator is null)
            {
                Console.Error.WriteLine($"[SplashWindow] Controls not found in UpdateProgress");
                return;
            }

            if (!string.IsNullOrEmpty(message))
            {
                statusText.Text = message;
            }

            progressIndicator.IsIndeterminate = false;
            progressIndicator.Value = Math.Clamp(percent, 0, 100);
        });
    }

    /// <summary>
    /// 更新状态文本
    /// </summary>
    public void UpdateStatus(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            if (statusText is null)
            {
                Console.Error.WriteLine($"[SplashWindow] StatusText not found in UpdateStatus");
                return;
            }
            statusText.Text = message;
        });
    }

    /// <summary>
    /// 报告阶段和进度（0-100）
    /// </summary>
    public void ReportStage(string stage, int progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            var progressIndicator = this.FindControl<ProgressBar>("ProgressIndicator");
            
            if (statusText is null || progressIndicator is null)
            {
                Console.Error.WriteLine($"[SplashWindow] Controls not found in ReportStage");
                return;
            }

            statusText.Text = stage;
            progressIndicator.IsIndeterminate = false;
            progressIndicator.Value = Math.Clamp(progress, 0, 100);
        });
    }

    /// <summary>
    /// 设置版本和开发代号
    /// </summary>
    public void SetVersionInfo(string version, string codename)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var versionText = this.FindControl<TextBlock>("VersionText");
            if (versionText is null)
            {
                Console.Error.WriteLine($"[SplashWindow] VersionText not found in SetVersionInfo");
                return;
            }
            versionText.Text = $"{version} ({codename})";
        });
    }

    /// <summary>
    /// 设置调试模式
    /// </summary>
    public void SetDebugMode(bool isDebugMode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            if (statusText is null)
            {
                Console.Error.WriteLine($"[SplashWindow] StatusText not found in SetDebugMode");
                return;
            }
            if (isDebugMode)
            {
                statusText.Text = "[Debug Mode] Splash Preview";
            }
        });
    }

    /// <summary>
    /// 根据阶段名称解析进度值
    /// </summary>
    private static int ResolveProgress(string stage)
    {
        return stage.ToLowerInvariant() switch
        {
            "initializing" => 10,
            "update" => 30,
            "plugins" => 50,
            "launch" => 70,
            "ready" => 100,
            _ => 0
        };
    }
}
