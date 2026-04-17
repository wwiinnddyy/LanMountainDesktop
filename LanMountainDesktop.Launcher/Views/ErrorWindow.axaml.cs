using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LanMountainDesktop.Launcher.Services;
using System.Diagnostics;

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
        _customHostPath = LoadCustomHostPathInternal();

        // 延迟到窗口加载完成后再初始化组件，确保视觉树已准备好
        this.Loaded += OnWindowLoaded;
        this.Opened += OnWindowOpened;
    }

    /// <summary>
    /// 窗口加载完成事件 - 视觉树已准备好
    /// </summary>
    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("[ErrorWindow] Window loaded, initializing components...");
        InitializeComponents();
    }

    /// <summary>
    /// 窗口打开事件
    /// </summary>
    private void OnWindowOpened(object? sender, EventArgs e)
    {
        Console.WriteLine("[ErrorWindow] Window opened and visible");
    }

    private void InitializeComponents()
    {
        Console.WriteLine("[ErrorWindow] Initializing components...");
        
        // 错误图标点击事件（进入调试模式 - 隐藏功能）
        var errorIconBorder = this.FindControl<Border>("ErrorIconBorder");
        if (errorIconBorder is not null)
        {
            errorIconBorder.PointerPressed += OnErrorIconClick;
            Console.WriteLine("[ErrorWindow] ErrorIconBorder event bound successfully");
        }
        else
        {
            Console.Error.WriteLine("[ErrorWindow] Failed to find ErrorIconBorder!");
        }

        // 按钮事件
        var retryButton = this.FindControl<Button>("RetryButton");
        var exitButton = this.FindControl<Button>("ExitButton");
        var openLogButton = this.FindControl<Button>("OpenLogButton");

        if (retryButton is not null)
        {
            retryButton.Click += OnRetryClick;
            Console.WriteLine("[ErrorWindow] RetryButton event bound");
        }
        else
        {
            Console.Error.WriteLine("[ErrorWindow] Failed to find RetryButton!");
        }

        if (exitButton is not null)
        {
            exitButton.Click += OnExitClick;
            Console.WriteLine("[ErrorWindow] ExitButton event bound");
        }
        else
        {
            Console.Error.WriteLine("[ErrorWindow] Failed to find ExitButton!");
        }

        if (openLogButton is not null)
        {
            openLogButton.Click += OnOpenLogClick;
            Console.WriteLine("[ErrorWindow] OpenLogButton event bound");
        }
        else
        {
            Console.Error.WriteLine("[ErrorWindow] Failed to find OpenLogButton!");
        }
        
        Console.WriteLine("[ErrorWindow] Components initialization completed");
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
    /// 设置调试模式
    /// </summary>
    public void SetDebugMode(bool isDebugMode)
    {
        _isDebugMode = isDebugMode;
        var titleText = this.FindControl<TextBlock>("TitleText");
        if (titleText is not null && isDebugMode)
        {
            titleText.Text = "[调试模式] 错误页面";
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

            // 保存开发模式状态和自定义路径
            SaveDevModeStateInternal(_devModeEnabled);
            SaveCustomHostPathInternal(_customHostPath);

            // 如果启用了开发模式且没有选择路径，自动扫描
            if (_devModeEnabled && string.IsNullOrEmpty(_customHostPath))
            {
                ScanDevPaths();
                // 扫描到路径后也保存
                if (!string.IsNullOrEmpty(_customHostPath))
                {
                    SaveCustomHostPathInternal(_customHostPath);
                }
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
    /// 获取配置存储的基础目录
    /// </summary>
    private static string GetConfigBaseDirectory()
    {
        try
        {
            // 优先使用 LocalApplicationData（用户状态）
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                var configDir = Path.Combine(appData, "LanMountainDesktop", ".launcher");
                return configDir;
            }
        }
        catch
        {
            // LocalApplicationData 不可用，回退到 Launcher 所在目录
        }

        // 回退方案：使用 Launcher 所在目录
        try
        {
            var launcherDir = AppContext.BaseDirectory;
            var configDir = Path.Combine(launcherDir, ".launcher");
            return configDir;
        }
        catch
        {
            // 最后的兜底：使用当前目录
            return Path.Combine(Directory.GetCurrentDirectory(), ".launcher");
        }
    }

    /// <summary>
    /// 确保配置目录存在
    /// </summary>
    private static bool EnsureConfigDirectory(string dirPath)
    {
        try
        {
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
                Console.WriteLine($"[ErrorWindow] Created config directory: {dirPath}");
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErrorWindow] Failed to create config directory: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 保存开发模式状态（内部方法）
    /// </summary>
    private static void SaveDevModeStateInternal(bool enabled)
    {
        try
        {
            var configDir = GetConfigBaseDirectory();
            if (!EnsureConfigDirectory(configDir))
            {
                Console.Error.WriteLine("[ErrorWindow] Cannot save dev mode: config directory unavailable");
                return;
            }

            var devModeFile = Path.Combine(configDir, "devmode.config");
            File.WriteAllText(devModeFile, enabled ? "1" : "0");
            Console.WriteLine($"[ErrorWindow] Dev mode state saved: {enabled}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErrorWindow] Failed to save dev mode state: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载开发模式状态（内部方法）
    /// </summary>
    private static bool LoadDevModeStateInternal()
    {
        try
        {
            var configDir = GetConfigBaseDirectory();
            var devModeFile = Path.Combine(configDir, "devmode.config");

            if (File.Exists(devModeFile))
            {
                var content = File.ReadAllText(devModeFile).Trim();
                var enabled = content == "1";
                Console.WriteLine($"[ErrorWindow] Dev mode state loaded: {enabled}");
                return enabled;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErrorWindow] Failed to load dev mode state: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// 保存自定义主程序路径（内部方法）
    /// </summary>
    private static void SaveCustomHostPathInternal(string? path)
    {
        try
        {
            var configDir = GetConfigBaseDirectory();
            if (!EnsureConfigDirectory(configDir))
            {
                Console.Error.WriteLine("[ErrorWindow] Cannot save custom path: config directory unavailable");
                return;
            }

            var hostPathFile = Path.Combine(configDir, "custom-host-path.config");
            File.WriteAllText(hostPathFile, path ?? string.Empty);
            Console.WriteLine($"[ErrorWindow] Custom host path saved: {path}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErrorWindow] Failed to save custom host path: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载自定义主程序路径（内部方法）
    /// </summary>
    private static string? LoadCustomHostPathInternal()
    {
        try
        {
            var configDir = GetConfigBaseDirectory();
            var hostPathFile = Path.Combine(configDir, "custom-host-path.config");

            if (File.Exists(hostPathFile))
            {
                var content = File.ReadAllText(hostPathFile).Trim();
                // 验证路径是否仍然有效
                if (!string.IsNullOrEmpty(content) && File.Exists(content))
                {
                    Console.WriteLine($"[ErrorWindow] Custom host path loaded: {content}");
                    return content;
                }

                // 路径已失效，清理配置文件
                if (!string.IsNullOrEmpty(content))
                {
                    Console.WriteLine($"[ErrorWindow] Custom host path is no longer valid: {content}");
                    try
                    {
                        File.Delete(hostPathFile);
                        Console.WriteLine("[ErrorWindow] Cleared invalid custom host path");
                    }
                    catch (Exception clearEx)
                    {
                        Console.Error.WriteLine($"[ErrorWindow] Failed to clear invalid host path: {clearEx.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErrorWindow] Failed to load custom host path: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 检查是否启用了开发模式（静态方法，启动时调用）
    /// </summary>
    public static bool CheckDevModeEnabled()
    {
        return LoadDevModeStateInternal();
    }

    /// <summary>
    /// 获取保存的自定义主程序路径（静态方法，启动时调用）
    /// </summary>
    public static string? GetSavedCustomHostPath()
    {
        return LoadCustomHostPathInternal();
    }

    private void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        _completionSource.TrySetResult(ErrorWindowResult.Retry);
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        _completionSource.TrySetResult(ErrorWindowResult.Exit);
    }

    /// <summary>
    /// 打开日志文件
    /// </summary>
    private async void OnOpenLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var logFilePath = Logger.GetLogFilePath();

            if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
            {
                // 如果没有日志文件，打开日志目录
                var logDir = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(logDir) && Directory.Exists(logDir))
                {
                    OpenFolder(logDir);
                }
                else
                {
                    // 尝试打开配置目录
                    var configDir = GetConfigBaseDirectory();
                    if (Directory.Exists(configDir))
                    {
                        OpenFolder(configDir);
                    }
                    else
                    {
                        Console.WriteLine("[ErrorWindow] No log file or directory available");
                    }
                }
                return;
            }

            Console.WriteLine($"[ErrorWindow] Opening log file: {logFilePath}");
            OpenFile(logFilePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErrorWindow] Failed to open log: {ex.Message}");
        }
    }

    /// <summary>
    /// 打开文件
    /// </summary>
    private static void OpenFile(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", filePath);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", filePath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErrorWindow] Failed to open file: {ex.Message}");
        }
    }

    /// <summary>
    /// 打开文件夹
    /// </summary>
    private static void OpenFolder(string folderPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", folderPath);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", folderPath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErrorWindow] Failed to open folder: {ex.Message}");
        }
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
