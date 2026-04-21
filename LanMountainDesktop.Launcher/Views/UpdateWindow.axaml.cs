using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace LanMountainDesktop.Launcher.Views;

/// <summary>
/// 更新进度窗口 - 用于 apply-update 命令模式下显示更新/插件升级进度
/// </summary>
public partial class UpdateWindow : Window
{
    public UpdateWindow()
    {
        AvaloniaXamlLoader.Load(this);
        InitializeEventHandlers();
    }

    /// <summary>
    /// 初始化事件处理程序
    /// </summary>
    private void InitializeEventHandlers()
    {
        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        if (minimizeButton != null)
        {
            minimizeButton.Click += (s, e) =>
            {
                this.WindowState = WindowState.Minimized;
            };
        }
    }

    /// <summary>
    /// 更新状态和进度
    /// </summary>
    public void Report(string stage, string message, int progressPercent = -1)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            var progressIndicator = this.FindControl<ProgressBar>("ProgressIndicator");
            var percentText = this.FindControl<TextBlock>("PercentText");

            if (statusText is null || progressIndicator is null || percentText is null)
            {
                Console.Error.WriteLine($"[UpdateWindow] Controls not found in Report: StatusText={statusText != null}, ProgressIndicator={progressIndicator != null}, PercentText={percentText != null}");
                return;
            }

            statusText.Text = message;

            if (progressPercent >= 0)
            {
                progressIndicator.IsIndeterminate = false;
                progressIndicator.Value = progressPercent;
                percentText.Text = $"{progressPercent}%";
            }
            else
            {
                progressIndicator.IsIndeterminate = true;
                percentText.Text = "";
            }
        });
    }

    /// <summary>
    /// 显示更新完成状态
    /// </summary>
    public void ReportComplete(bool success, string? errorMessage = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            var progressIndicator = this.FindControl<ProgressBar>("ProgressIndicator");
            var percentText = this.FindControl<TextBlock>("PercentText");
            var titleText = this.FindControl<TextBlock>("TitleText");

            if (statusText is null || progressIndicator is null || percentText is null || titleText is null)
            {
                Console.Error.WriteLine($"[UpdateWindow] Controls not found in ReportComplete");
                return;
            }

            progressIndicator.IsIndeterminate = false;
            progressIndicator.Value = 100;
            percentText.Text = "100%";

            if (success)
            {
                statusText.Text = "更新完成";
            }
            else
            {
                titleText.Text = "更新失败";
                statusText.Text = errorMessage ?? "更新过程中发生错误";
            }
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
            var titleText = this.FindControl<TextBlock>("TitleText");

            if (statusText is null || titleText is null)
            {
                Console.Error.WriteLine($"[UpdateWindow] Controls not found in SetDebugMode");
                return;
            }

            if (isDebugMode)
            {
                titleText.Text = "[调试模式] 更新页面";
                statusText.Text = "预览更新进度界面";
            }
        });
    }
}
