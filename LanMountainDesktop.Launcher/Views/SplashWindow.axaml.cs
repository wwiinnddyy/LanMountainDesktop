using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Resources;
using LanMountainDesktop.Launcher.Shell;

namespace LanMountainDesktop.Launcher.Views;

public partial class SplashWindow : Window, ISplashStageReporter
{
    private const int DebugModeClickThreshold = 5;
    private static readonly TimeSpan FadeAnimationDuration = TimeSpan.FromMilliseconds(160);

    private readonly object _dismissSync = new();
    private int _versionTextClickCount;
    private bool _isDebugModeOpened;
    private bool _isOpened;
    private Task? _dismissTask;

    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnWindowLoaded;
        Opened += OnWindowOpened;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        InitializeBackgroundImage();

        if (this.FindControl<Border>("VersionTextBorder") is { } versionBorder)
        {
            versionBorder.PointerPressed += OnVersionTextClick;
        }
    }

    private void InitializeBackgroundImage()
    {
        try
        {
            ResetBackgroundImage();

            var imageInfo = LauncherBackgroundService.LoadBackgroundImage();
            if (imageInfo is { IsValid: true, Bitmap: not null })
            {
                if (this.FindControl<Image>("BackgroundImage") is { } backgroundImage)
                {
                    backgroundImage.Source = imageInfo.Bitmap;
                    backgroundImage.IsVisible = true;
                    backgroundImage.Opacity = 1;
                }

                Logger.Info("[SplashWindow] Background image loaded.");
            }
            else if (imageInfo is { Exists: true, IsValid: false })
            {
                Logger.Warn($"[SplashWindow] Background image validation failed: {imageInfo.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[SplashWindow] Failed to load background image: {ex.Message}");
        }
    }

    private void ResetBackgroundImage()
    {
        if (this.FindControl<Image>("BackgroundImage") is { } backgroundImage)
        {
            backgroundImage.Source = null;
            backgroundImage.IsVisible = false;
            backgroundImage.Opacity = 0;
        }
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_isOpened)
        {
            return;
        }

        _isOpened = true;

        Opacity = 0d;
        await AnimateOpacityAsync(0d, 1d, FadeAnimationDuration).ConfigureAwait(false);
    }

    public Task DismissAsync()
    {
        lock (_dismissSync)
        {
            return _dismissTask ??= DismissCoreAsync();
        }
    }

    private async Task DismissCoreAsync()
    {
        try
        {
            var animationState = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsHitTestVisible = false;

                if (!IsVisible)
                {
                    HideAndCloseOnUiThread();
                    return (ShouldAnimate: false, StartOpacity: 0d);
                }

                return (ShouldAnimate: true, StartOpacity: Opacity);
            });

            if (!animationState.ShouldAnimate)
            {
                return;
            }

            await AnimateOpacityAsync(
                    animationState.StartOpacity,
                    0d,
                    FadeAnimationDuration,
                    HideAndCloseOnUiThread)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[SplashWindow] Fade-out failed; closing immediately: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(HideAndCloseOnUiThread);
        }
    }

    private void HideAndCloseOnUiThread()
    {
        Dispatcher.UIThread.VerifyAccess();
        IsHitTestVisible = false;

        try
        {
            Hide();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[SplashWindow] Failed to hide splash window: {ex.Message}");
        }

        try
        {
            Close();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[SplashWindow] Failed to close splash window: {ex.Message}");
        }

        try
        {
            if (IsVisible)
            {
                Hide();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[SplashWindow] Failed to enforce hidden splash state: {ex.Message}");
        }
    }

    public void Report(string stage, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<TextBlock>("StatusText") is { } statusText)
            {
                statusText.Text = message;
            }

            if (this.FindControl<ProgressBar>("ProgressIndicator") is { } progressIndicator)
            {
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
            }
        });
    }

    public void ReportStage(string stage, int progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<TextBlock>("StatusText") is { } statusText)
            {
                statusText.Text = stage;
            }

            if (this.FindControl<ProgressBar>("ProgressIndicator") is { } progressIndicator)
            {
                progressIndicator.IsIndeterminate = false;
                progressIndicator.Value = Math.Clamp(progress, 0, 100);
            }
        });
    }

    public void UpdateProgress(int percent, string? message = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrWhiteSpace(message) &&
                this.FindControl<TextBlock>("StatusText") is { } statusText)
            {
                statusText.Text = message;
            }

            if (this.FindControl<ProgressBar>("ProgressIndicator") is { } progressIndicator)
            {
                progressIndicator.IsIndeterminate = false;
                progressIndicator.Value = Math.Clamp(percent, 0, 100);
            }
        });
    }

    public void UpdateStatus(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<TextBlock>("StatusText") is { } statusText)
            {
                statusText.Text = message;
            }
        });
    }

    public void SetVersionInfo(string version, string codename)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<TextBlock>("VersionText") is { } versionText)
            {
                versionText.Text = $"{version} ({codename})";
            }
        });
    }

    public void SetDebugMode(bool isDebugMode)
    {
        if (!isDebugMode)
        {
            return;
        }

        UpdateStatus(Strings.Splash_DebugPreview);
    }

    private void OnVersionTextClick(object? sender, PointerPressedEventArgs e)
    {
        if (_isDebugModeOpened)
        {
            return;
        }

        _versionTextClickCount++;
        if (_versionTextClickCount >= DebugModeClickThreshold)
        {
            OpenDebugWindow();
        }
    }

    private async void OpenDebugWindow()
    {
        _isDebugModeOpened = true;

        try
        {
            var debugWindow = new ErrorDebugWindow(
                ErrorWindow.CheckDevModeEnabled(),
                ErrorWindow.GetSavedCustomHostPath())
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            debugWindow.Closed += (_, _) =>
            {
                if (debugWindow.WasAccepted)
                {
                    LauncherDebugSettingsStore.Save(new LauncherDebugSettings(
                        debugWindow.IsDevModeEnabled,
                        debugWindow.SelectedHostPath));
                }

                InitializeBackgroundImage();
                _isDebugModeOpened = false;
                _versionTextClickCount = 0;
            };

            await debugWindow.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SplashWindow] Failed to open debug window: {ex}");
            _isDebugModeOpened = false;
            _versionTextClickCount = 0;
        }
    }

    private async Task AnimateOpacityAsync(
        double from,
        double to,
        TimeSpan duration,
        Action? completed = null)
    {
        await AnimateAsync(progress =>
        {
            Opacity = from + ((to - from) * progress);
        }, duration, EaseOutCubic, completed).ConfigureAwait(false);
    }

    private async Task AnimateAsync(
        Action<double> update,
        TimeSpan duration,
        Func<double, double> easing,
        Action? completed)
    {
        if (duration <= TimeSpan.Zero)
        {
            await Dispatcher.UIThread.InvokeAsync(() => ApplyFinalFrame(update, completed));
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            var raw = stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds;
            var progress = easing(Math.Clamp(raw, 0d, 1d));
            await Dispatcher.UIThread.InvokeAsync(() => update(progress));
            await Task.Delay(16).ConfigureAwait(false);
        }

        await Dispatcher.UIThread.InvokeAsync(() => ApplyFinalFrame(update, completed));
    }

    private static void ApplyFinalFrame(Action<double> update, Action? completed)
    {
        try
        {
            update(1d);
        }
        finally
        {
            completed?.Invoke();
        }
    }

    private static int ResolveProgress(string stage)
    {
        return stage.ToLowerInvariant() switch
        {
            "initializing" => 10,
            "settings" => 25,
            "update" => 30,
            "plugins" => 50,
            "ui" => 65,
            "shell" => 80,
            "activation" => 90,
            "ready" => 100,
            _ => 0
        };
    }

    private static double EaseOutCubic(double value)
    {
        var inverse = 1d - value;
        return 1d - (inverse * inverse * inverse);
    }
}
