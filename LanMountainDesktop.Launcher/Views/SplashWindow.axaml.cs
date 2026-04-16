using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Services;

namespace LanMountainDesktop.Launcher.Views;

/// <summary>
/// 启动画面窗口 - 简洁设计
/// </summary>
public partial class SplashWindow : Window, ISplashStageReporter
{
    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 更新进度和状态
    /// </summary>
    public void Report(string stage, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var statusText = this.GetControl<TextBlock>("StatusText");
            var progressIndicator = this.GetControl<ProgressBar>("ProgressIndicator");

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
            var statusText = this.GetControl<TextBlock>("StatusText");
            var progressIndicator = this.GetControl<ProgressBar>("ProgressIndicator");

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
            var statusText = this.GetControl<TextBlock>("StatusText");
            statusText.Text = message;
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
