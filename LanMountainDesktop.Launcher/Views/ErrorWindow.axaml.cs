using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LanMountainDesktop.Launcher.Services;

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
        if (this.FindControl<TextBlock>("ErrorMessageText") is { } errorText)
        {
            errorText.Text = message;
        }
    }

    public void SetDebugMode(bool isDebugMode)
    {
        _isDebugMode = isDebugMode;
        if (isDebugMode && this.FindControl<TextBlock>("TitleText") is { } titleText)
        {
            titleText.Text = "[Debug] Launcher error";
        }
    }

    public void ConfigureForHostNotFound()
    {
        ApplyActionLayout(
            title: "Launcher could not find the desktop executable",
            suggestion: "Pick another executable in debug mode, inspect logs, or retry after fixing the deployment path.",
            primaryLabel: "Retry",
            primaryAction: ErrorWindowResult.Retry,
            secondaryLabel: null,
            secondaryAction: null);
    }

    public void ConfigureForGenericFailure(bool allowRetry)
    {
        ApplyActionLayout(
            title: "Launcher could not confirm startup",
            suggestion: allowRetry
                ? "Inspect logs, then retry once the previous startup attempt has fully finished."
                : "Inspect logs or exit. Launcher will avoid creating another desktop process while the old one is still running.",
            primaryLabel: allowRetry ? "Retry" : "Activate",
            primaryAction: allowRetry ? ErrorWindowResult.Retry : ErrorWindowResult.ActivateExisting,
            secondaryLabel: allowRetry ? null : "Wait",
            secondaryAction: allowRetry ? null : ErrorWindowResult.ContinueWaiting);
    }

    public void ConfigureForRunningHostFailure(int? hostPid)
    {
        var pidHint = hostPid is > 0 ? $" Current host PID: {hostPid}." : string.Empty;
        ApplyActionLayout(
            title: "Startup is still pending",
            suggestion: $"The desktop process is still running, so Launcher will not start a second instance.{pidHint}",
            primaryLabel: "Activate",
            primaryAction: ErrorWindowResult.ActivateExisting,
            secondaryLabel: "Wait",
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

        if (this.FindControl<TextBlock>("SuggestionText") is { } suggestionText)
        {
            suggestionText.Text = suggestion;
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

            var configDirectory = GetConfigBaseDirectory();
            if (Directory.Exists(configDirectory))
            {
                OpenPath(configDirectory);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ErrorWindow] Failed to open log path: {ex}");
        }

        await Task.CompletedTask;
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
