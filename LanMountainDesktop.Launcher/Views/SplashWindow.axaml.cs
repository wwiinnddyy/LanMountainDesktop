using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LanMountainDesktop.Launcher.Services;

namespace LanMountainDesktop.Launcher.Views;

internal partial class SplashWindow : Window, ISplashStageReporter
{
    private static readonly (string Stage, string Label, double Progress)[] StageMap =
    [
        ("bootstrap", "正在初始化...", 10),
        ("silentUpdate", "正在应用更新...", 35),
        ("pluginTasks", "正在处理插件...", 65),
        ("launchHost", "正在启动...", 90),
    ];

    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public void Report(string stage, string message)
    {
        var (label, progress) = ResolveStageInfo(stage);

        var stageText = this.GetControl<TextBlock>("StageText");
        var detailText = this.GetControl<TextBlock>("DetailText");
        var progressIndicator = this.GetControl<ProgressBar>("ProgressIndicator");

        stageText.Text = label;
        detailText.Text = message;
        progressIndicator.IsIndeterminate = false;
        progressIndicator.Value = progress;
    }

    private static (string Label, double Progress) ResolveStageInfo(string stage)
    {
        foreach (var (s, label, progress) in StageMap)
        {
            if (string.Equals(s, stage, StringComparison.OrdinalIgnoreCase))
            {
                return (label, progress);
            }
        }

        return (stage, 0);
    }
}
