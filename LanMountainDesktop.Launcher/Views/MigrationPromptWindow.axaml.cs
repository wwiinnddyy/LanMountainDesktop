using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LanMountainDesktop.Launcher.Services;

namespace LanMountainDesktop.Launcher.Views;

/// <summary>
/// 迁移提示窗口 - 提示用户卸载旧版本
/// </summary>
public partial class MigrationPromptWindow : Window
{
    private readonly TaskCompletionSource<MigrationResult> _completionSource = new();
    private LegacyVersionInfo? _legacyInfo;

    public MigrationPromptWindow()
    {
        AvaloniaXamlLoader.Load(this);
        InitializeEventHandlers();
    }

    /// <summary>
    /// 设置老版本信息
    /// </summary>
    public void SetLegacyInfo(LegacyVersionInfo info)
    {
        _legacyInfo = info;

        // 更新 UI
        var versionText = this.FindControl<TextBlock>("VersionText");
        var pathText = this.FindControl<TextBlock>("PathText");
        var typeText = this.FindControl<TextBlock>("TypeText");
        var descriptionText = this.FindControl<TextBlock>("DescriptionText");

        if (versionText != null)
        {
            versionText.Text = info.Version;
        }

        if (pathText != null)
        {
            pathText.Text = info.InstallPath;
        }

        if (typeText != null)
        {
            typeText.Text = info.InstallType switch
            {
                LegacyInstallType.Registry => "安装版",
                LegacyInstallType.Portable => "便携版",
                _ => "未知"
            };
        }

        if (descriptionText != null)
        {
            descriptionText.Text = $"检测到您的系统中安装了旧版本的阑山桌面（{info.Version}）。新版本采用了全新的架构，建议卸载旧版本以获得更好的体验。";
        }
    }

    /// <summary>
    /// 初始化事件处理程序
    /// </summary>
    private void InitializeEventHandlers()
    {
        var showLocationButton = this.FindControl<Button>("ShowLocationButton");
        var skipButton = this.FindControl<Button>("SkipButton");
        var uninstallButton = this.FindControl<Button>("UninstallButton");

        if (showLocationButton != null)
        {
            showLocationButton.Click += OnShowLocationClick;
        }

        if (skipButton != null)
        {
            skipButton.Click += OnSkipClick;
        }

        if (uninstallButton != null)
        {
            uninstallButton.Click += OnUninstallClick;
        }
    }

    /// <summary>
    /// 查看位置按钮点击
    /// </summary>
    private void OnShowLocationClick(object? sender, RoutedEventArgs e)
    {
        if (_legacyInfo != null)
        {
            LegacyVersionDetector.ShowInExplorer(_legacyInfo.InstallPath);
        }
    }

    /// <summary>
    /// 跳过按钮点击
    /// </summary>
    private void OnSkipClick(object? sender, RoutedEventArgs e)
    {
        _completionSource.TrySetResult(MigrationResult.Skipped);
        Close();
    }

    /// <summary>
    /// 卸载按钮点击
    /// </summary>
    private void OnUninstallClick(object? sender, RoutedEventArgs e)
    {
        if (_legacyInfo != null)
        {
            LegacyVersionDetector.OpenUninstallInterface(_legacyInfo);
        }

        _completionSource.TrySetResult(MigrationResult.UninstallOpened);
        Close();
    }

    /// <summary>
    /// 等待用户选择
    /// </summary>
    public Task<MigrationResult> WaitForChoiceAsync()
    {
        return _completionSource.Task;
    }

    /// <summary>
    /// 窗口关闭事件
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 如果还没有完成，标记为跳过
        if (!_completionSource.Task.IsCompleted)
        {
            _completionSource.TrySetResult(MigrationResult.Skipped);
        }

        base.OnClosing(e);
    }
}

/// <summary>
/// 迁移结果
/// </summary>
public enum MigrationResult
{
    /// <summary>
    /// 用户选择跳过
    /// </summary>
    Skipped,

    /// <summary>
    /// 已打开卸载界面
    /// </summary>
    UninstallOpened
}
