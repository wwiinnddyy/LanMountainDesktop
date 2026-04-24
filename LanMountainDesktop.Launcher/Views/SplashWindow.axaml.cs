using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Services;

namespace LanMountainDesktop.Launcher.Views;

public partial class SplashWindow : Window, ISplashStageReporter
{
    private const int DebugModeClickThreshold = 5;
    private static readonly TimeSpan FadeAnimationDuration = TimeSpan.FromMilliseconds(160);

    private int _versionTextClickCount;
    private bool _isDebugModeOpened;
    private bool _isOpened;
    private bool _dismissed;

    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnWindowLoaded;
        Opened += OnWindowOpened;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Border>("VersionTextBorder") is { } versionBorder)
        {
            versionBorder.PointerPressed += OnVersionTextClick;
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

    public async Task DismissAsync()
    {
        if (_dismissed)
        {
            return;
        }

        _dismissed = true;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(async () => await DismissAsync());
            return;
        }

        await AnimateOpacityAsync(Opacity, 0d, FadeAnimationDuration).ConfigureAwait(false);
        Close();
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

        UpdateStatus("[Debug Mode] Splash Preview");
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

    private async Task AnimateOpacityAsync(double from, double to, TimeSpan duration)
    {
        await AnimateAsync(progress =>
        {
            Opacity = from + ((to - from) * progress);
        }, duration, EaseOutCubic).ConfigureAwait(false);
    }

    private async Task AnimateAsync(Action<double> update, TimeSpan duration, Func<double, double> easing)
    {
        if (duration <= TimeSpan.Zero)
        {
            await Dispatcher.UIThread.InvokeAsync(() => update(1d));
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

        await Dispatcher.UIThread.InvokeAsync(() => update(1d));
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
