using System.Diagnostics;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.Launcher.Resources;
using LanMountainDesktop.Launcher.Infrastructure;

namespace LanMountainDesktop.Launcher.Views;

public partial class ErrorWindow : Window
{
    private const int DebugModeClickThreshold = 5;

    private readonly TaskCompletionSource<ErrorWindowResult> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _iconClickCount;
    private bool _isDebugMode;
    private bool _devModeEnabled;
    private string? _customHostPath;
    private ErrorWindowResult _primaryAction = ErrorWindowResult.Retry;
    private ErrorWindowResult? _secondaryAction;

    public ErrorWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _devModeEnabled = LoadDevModeStateInternal();
        _customHostPath = LoadCustomHostPathInternal();

        Loaded += OnWindowLoaded;
        Closed += (_, _) => _completionSource.TrySetResult(ErrorWindowResult.Exit);
        ConfigureForGenericFailure(allowRetry: true);
    }

    public void SetErrorMessage(string message)
    {
        var normalizedMessage = string.IsNullOrWhiteSpace(message)
            ? Strings.Error_MessageNotReached
            : message.Trim();
        var firstLine = normalizedMessage
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? normalizedMessage;

        if (this.FindControl<TextBlock>("ErrorMessageText") is { } errorText)
        {
            errorText.Text = firstLine;
        }

        if (this.FindControl<TextBox>("ErrorDetailsTextBox") is { } detailsTextBox)
        {
            detailsTextBox.Text = normalizedMessage;
        }
    }

    public void SetDebugMode(bool isDebugMode)
    {
        _isDebugMode = isDebugMode;
        if (isDebugMode && this.FindControl<TextBlock>("TitleText") is { } titleText)
        {
            titleText.Text = Strings.Error_DebugTitle;
        }
    }

    public void ConfigureForHostNotFound()
    {
        ApplyActionLayout(
            title: Strings.Error_HostNotFoundTitle,
            suggestion: Strings.Error_HostNotFoundMessage,
            primaryLabel: Strings.Error_ButtonRetry,
            primaryAction: ErrorWindowResult.Retry,
            secondaryLabel: null,
            secondaryAction: null);
    }

    public void ConfigureForGenericFailure(bool allowRetry)
    {
        ApplyActionLayout(
            title: Strings.Error_TitleCannotConfirm,
            suggestion: allowRetry
                ? Strings.Error_GenericRetryMessage
                : Strings.Error_GenericNoRetryMessage,
            primaryLabel: allowRetry ? Strings.Error_ButtonRetry : Strings.Error_ButtonActivate,
            primaryAction: allowRetry ? ErrorWindowResult.Retry : ErrorWindowResult.ActivateExisting,
            secondaryLabel: allowRetry ? null : Strings.Error_ButtonWait,
            secondaryAction: allowRetry ? null : ErrorWindowResult.ContinueWaiting);
    }

    public void ConfigureForRunningHostFailure(int? hostPid)
    {
        var suggestion = hostPid is > 0
            ? string.Format(Strings.Error_PendingMessageWithPid, hostPid)
            : Strings.Error_PendingMessage;
        ApplyActionLayout(
            title: Strings.Error_PendingTitle,
            suggestion: suggestion,
            primaryLabel: Strings.Error_ButtonActivate,
            primaryAction: ErrorWindowResult.ActivateExisting,
            secondaryLabel: Strings.Error_ButtonWait,
            secondaryAction: ErrorWindowResult.ContinueWaiting);
    }

    public string? GetCustomHostPath() => _customHostPath;

    public bool IsDevModeEnabled() => _devModeEnabled;

    public Task<ErrorWindowResult> WaitForChoiceAsync() => _completionSource.Task;

    public static bool CheckDevModeEnabled() => LoadDevModeStateInternal();

    public static string? GetSavedCustomHostPath() => LoadCustomHostPathInternal();

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Border>("ErrorIconBorder") is { } errorIconBorder)
        {
            errorIconBorder.PointerPressed += OnErrorIconClick;
        }

        if (this.FindControl<Button>("PrimaryActionButton") is { } primaryActionButton)
        {
            primaryActionButton.Click += OnPrimaryActionClick;
        }

        if (this.FindControl<Button>("SecondaryActionButton") is { } secondaryActionButton)
        {
            secondaryActionButton.Click += OnSecondaryActionClick;
        }

        if (this.FindControl<Button>("ExitButton") is { } exitButton)
        {
            exitButton.Click += (_, _) => _completionSource.TrySetResult(ErrorWindowResult.Exit);
        }

        if (this.FindControl<Button>("OpenLogButton") is { } openLogButton)
        {
            openLogButton.Click += OnOpenLogClick;
        }

        if (this.FindControl<Button>("CopyDetailsButton") is { } copyDetailsButton)
        {
            copyDetailsButton.Click += OnCopyDetailsClick;
        }
    }

    private void ApplyActionLayout(
        string title,
        string suggestion,
        string primaryLabel,
        ErrorWindowResult primaryAction,
        string? secondaryLabel,
        ErrorWindowResult? secondaryAction)
    {
        _primaryAction = primaryAction;
        _secondaryAction = secondaryAction;

        if (this.FindControl<TextBlock>("TitleText") is { } titleText && !_isDebugMode)
        {
            titleText.Text = title;
        }

        if (this.FindControl<FAInfoBar>("SuggestionInfoBar") is { } suggestionInfoBar)
        {
            suggestionInfoBar.Message = suggestion;
        }

        if (this.FindControl<Button>("PrimaryActionButton") is { } primaryButton)
        {
            primaryButton.Content = primaryLabel;
        }

        if (this.FindControl<Button>("SecondaryActionButton") is { } secondaryButton)
        {
            secondaryButton.IsVisible = !string.IsNullOrWhiteSpace(secondaryLabel);
            secondaryButton.Content = secondaryLabel ?? string.Empty;
        }
    }

    private void OnPrimaryActionClick(object? sender, RoutedEventArgs e)
    {
        _completionSource.TrySetResult(_primaryAction);
    }

    private void OnSecondaryActionClick(object? sender, RoutedEventArgs e)
    {
        _completionSource.TrySetResult(_secondaryAction ?? ErrorWindowResult.Exit);
    }

    private void OnErrorIconClick(object? sender, PointerPressedEventArgs e)
    {
        _iconClickCount++;
        if (_iconClickCount >= DebugModeClickThreshold && !_isDebugMode)
        {
            EnterDebugMode();
        }
    }

    private async void EnterDebugMode()
    {
        _isDebugMode = true;

        var debugWindow = new ErrorDebugWindow(_devModeEnabled, _customHostPath)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        debugWindow.Closed += (_, _) =>
        {
            if (!debugWindow.WasAccepted)
            {
                _isDebugMode = false;
                _iconClickCount = 0;
                return;
            }

            _devModeEnabled = debugWindow.IsDevModeEnabled;
            _customHostPath = debugWindow.SelectedHostPath;

            if (_devModeEnabled && string.IsNullOrWhiteSpace(_customHostPath))
            {
                ScanDevPaths();
            }

            LauncherDebugSettingsStore.Save(new LauncherDebugSettings(_devModeEnabled, _customHostPath));

            _isDebugMode = false;
            _iconClickCount = 0;
        };

        await debugWindow.ShowDialog(this);
    }

    private async void OnOpenLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // 优先打开主程序崩溃转储目录（最有诊断价值）
            var crashDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanMountainDesktop", "crashes");
            if (Directory.Exists(crashDir) && Directory.GetFiles(crashDir, "crash-*.txt").Length > 0)
            {
                OpenPath(crashDir);
                return;
            }

            // 其次打开主程序日志目录
            var hostLogDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanMountainDesktop", "log");
            if (Directory.Exists(hostLogDir) && Directory.GetFiles(hostLogDir).Length > 0)
            {
                OpenPath(hostLogDir);
                return;
            }

            // 回退到启动器日志文件
            var logFilePath = Logger.GetLogFilePath();
            if (!string.IsNullOrWhiteSpace(logFilePath) && File.Exists(logFilePath))
            {
                OpenPath(logFilePath);
                return;
            }

            var logDirectory = !string.IsNullOrWhiteSpace(logFilePath)
                ? Path.GetDirectoryName(logFilePath)
                : null;
            if (!string.IsNullOrWhiteSpace(logDirectory) && Directory.Exists(logDirectory))
            {
                OpenPath(logDirectory);
                return;
            }

            // 最后回退到配置目录
            var configDirectory = GetConfigBaseDirectory();
            if (Directory.Exists(configDirectory))
            {
                OpenPath(configDirectory);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to open log path: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    private async void OnCopyDetailsClick(object? sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var originalContent = button?.Content;
        try
        {
            var details = GetDetailsText();
            if (string.IsNullOrWhiteSpace(details))
            {
                ShowCopyFeedback(button, originalContent, false, "No content to copy");
                return;
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                // 剪贴板不可用（窗口未激活/会话限制），写入临时文件作为兜底
                var filePath = await WriteToTempFileAsync(details).ConfigureAwait(true);
                ShowCopyFeedback(button, originalContent, true, $"Saved to {Path.GetFileName(filePath)}");
                return;
            }

            try
            {
                await clipboard.SetTextAsync(details);
                ShowCopyFeedback(button, originalContent, true, "Copied");
            }
            catch (Exception clipboardEx)
            {
                // 剪贴板服务异常（组策略/RDP 限制等），写入临时文件兜底
                Logger.Warn($"Clipboard SetTextAsync failed: {clipboardEx.Message}. Falling back to temp file.");
                var filePath = await WriteToTempFileAsync(details).ConfigureAwait(true);
                ShowCopyFeedback(button, originalContent, true, $"Saved to {Path.GetFileName(filePath)}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to copy diagnostics.", ex);
            try
            {
                var details = GetDetailsText();
                if (!string.IsNullOrWhiteSpace(details))
                {
                    await WriteToTempFileAsync(details).ConfigureAwait(true);
                }
            }
            catch (Exception fallbackEx)
            {
                Logger.Warn($"Temp file fallback also failed: {fallbackEx.Message}");
            }
            ShowCopyFeedback(button, originalContent, false, "Copy failed");
        }
    }

    private string GetDetailsText()
    {
        var details = this.FindControl<TextBox>("ErrorDetailsTextBox")?.Text;
        if (string.IsNullOrWhiteSpace(details))
        {
            details = this.FindControl<TextBlock>("ErrorMessageText")?.Text;
        }
        return details ?? string.Empty;
    }

    private static async Task<string> WriteToTempFileAsync(string content)
    {
        var tempDir = Path.GetTempPath();
        var fileName = $"LanDesktopError_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        var path = Path.Combine(tempDir, fileName);
        await File.WriteAllTextAsync(path, content, Encoding.UTF8).ConfigureAwait(false);
        Logger.Info($"Error details written to temp file: {path}");
        return path;
    }

    private void ShowCopyFeedback(Button? button, object? originalContent, bool success, string message)
    {
        if (button is null)
        {
            return;
        }

        try
        {
            button.Content = message;
            button.IsEnabled = false;
            DispatcherTimer.RunOnce(() =>
            {
                try
                {
                    button.Content = originalContent;
                    button.IsEnabled = true;
                }
                catch
                {
                    // 窗口可能已关闭，忽略
                }
            }, TimeSpan.FromSeconds(2));
        }
        catch
        {
            // 忽略反馈失败
        }
    }

    private void ScanDevPaths()
    {
        var executable = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            Path.Combine(AppContext.BaseDirectory, "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(AppContext.BaseDirectory, "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable)
        };

        foreach (var candidate in candidatePaths.Select(Path.GetFullPath).Distinct())
        {
            if (File.Exists(candidate))
            {
                _customHostPath = candidate;
                break;
            }
        }
    }

    private static void OpenPath(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", path);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            Process.Start("xdg-open", path);
        }
    }

    private static string GetConfigBaseDirectory()
    {
        return LauncherDebugSettingsStore.ConfigBaseDirectory;
    }

    private static bool LoadDevModeStateInternal()
    {
        return LauncherDebugSettingsStore.IsDevModeEnabled();
    }

    private static string? LoadCustomHostPathInternal()
    {
        return LauncherDebugSettingsStore.GetSavedCustomHostPath();
    }
}

public enum ErrorWindowResult
{
    Retry,
    Exit,
    ActivateExisting,
    ContinueWaiting
}
