using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace LanMountainDesktop.Launcher.Views;

/// <summary>
/// 错误窗口 - 显示启动失败信息，支持调试模式（隐藏入口）
/// </summary>
public partial class ErrorWindow : Window
{
    private readonly TaskCompletionSource<ErrorWindowResult> _completionSource = new();
    private int _iconClickCount = 0;
    private const int DebugModeClickThreshold = 5;
    private bool _isDebugMode = false;
    private string? _customHostPath;
    private bool _devModeEnabled;

    public ErrorWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // 先加载保存的状态
        _devModeEnabled = LoadDevModeStateInternal();

        InitializeComponents();
    }

    private void InitializeComponents()
    {
        // 错误图标点击事件（进入调试模式 - 隐藏功能）
        var errorIconBorder = this.FindControl<Border>("ErrorIconBorder");
        if (errorIconBorder is not null)
        {
            errorIconBorder.PointerPressed += OnErrorIconClick;
        }

        // 按钮事件
        var retryButton = this.FindControl<Button>("RetryButton");
        var exitButton = this.FindControl<Button>("ExitButton");

        if (retryButton is not null)
        {
            retryButton.Click += OnRetryClick;
        }

        if (exitButton is not null)
        {
            exitButton.Click += OnExitClick;
        }
    }

    /// <summary>
    /// 设置错误消息
    /// </summary>
    public void SetErrorMessage(string message)
    {
        var errorText = this.FindControl<TextBlock>("ErrorMessageText");
        if (errorText is not null)
        {
            errorText.Text = message;
        }
    }

    /// <summary>
    /// 获取用户选择的主程序路径
    /// </summary>
    public string? GetCustomHostPath() => _customHostPath;

    /// <summary>
    /// 是否启用了开发模式
    /// </summary>
    public bool IsDevModeEnabled() => _devModeEnabled;

    /// <summary>
    /// 等待用户选择
    /// </summary>
    public Task<ErrorWindowResult> WaitForChoiceAsync()
    {
        return _completionSource.Task;
    }

    /// <summary>
    /// 错误图标点击事件 - 连续点击 5 次进入调试模式（隐藏功能）
    /// </summary>
    private void OnErrorIconClick(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        _iconClickCount++;

        if (_iconClickCount >= DebugModeClickThreshold && !_isDebugMode)
        {
            EnterDebugMode();
        }
    }

    /// <summary>
    /// 进入调试模式 - 显示调试窗口
    /// </summary>
    private async void EnterDebugMode()
    {
        _isDebugMode = true;

        // 创建并显示调试窗口
        var debugWindow = new ErrorDebugWindow(_devModeEnabled, _customHostPath)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        // 订阅调试窗口关闭事件
        debugWindow.Closed += (s, e) =>
        {
            // 更新状态
            _devModeEnabled = debugWindow.IsDevModeEnabled;
            _customHostPath = debugWindow.SelectedHostPath;

            // 保存开发模式状态
            SaveDevModeStateInternal(_devModeEnabled);

            // 如果启用了开发模式且没有选择路径，自动扫描
            if (_devModeEnabled && string.IsNullOrEmpty(_customHostPath))
            {
                ScanDevPaths();
            }
        };

        await debugWindow.ShowDialog(this);
    }

    /// <summary>
    /// 扫描开发路径
    /// </summary>
    private void ScanDevPaths()
    {
        var executable = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";
        var possiblePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            Path.Combine(AppContext.BaseDirectory, "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(AppContext.BaseDirectory, "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            Path.Combine(AppContext.BaseDirectory, "..", "dev-test", "app-1.0.0-dev", executable),
        };

        foreach (var path in possiblePaths.Select(Path.GetFullPath).Distinct())
        {
            if (File.Exists(path))
            {
                _customHostPath = path;
                break;
            }
        }
    }

    /// <summary>
    /// 保存开发模式状态（内部方法）
    /// </summary>
    private static void SaveDevModeStateInternal(bool enabled)
    {
        try
        {
            var devModeFile = GetDevModeFilePath();
            var dir = Path.GetDirectoryName(devModeFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(devModeFile, enabled ? "1" : "0");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save dev mode state: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载开发模式状态（内部方法）
    /// </summary>
    private static bool LoadDevModeStateInternal()
    {
        try
        {
            var devModeFile = GetDevModeFilePath();
            if (File.Exists(devModeFile))
            {
                var content = File.ReadAllText(devModeFile).Trim();
                return content == "1";
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load dev mode state: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// 获取开发模式状态文件路径
    /// </summary>
    private static string GetDevModeFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "LanMountainDesktop", ".launcher", "devmode.config");
    }

    /// <summary>
    /// 检查是否启用了开发模式（静态方法，启动时调用）
    /// </summary>
    public static bool CheckDevModeEnabled()
    {
        return LoadDevModeStateInternal();
    }

    private void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        _completionSource.TrySetResult(ErrorWindowResult.Retry);
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        _completionSource.TrySetResult(ErrorWindowResult.Exit);
    }
}

/// <summary>
/// 错误窗口用户选择结果
/// </summary>
public enum ErrorWindowResult
{
    /// <summary>
    /// 重试
    /// </summary>
    Retry,

    /// <summary>
    /// 退出
    /// </summary>
    Exit
}
