using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Views;

public partial class SplashWindow : Window, ISplashStageReporter
{
    private const int DebugModeClickThreshold = 5;
    private static readonly TimeSpan FadeAnimationDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan SlideAnimationDuration = TimeSpan.FromMilliseconds(260);

    private readonly StartupVisualMode _mode;
    private int _versionTextClickCount;
    private bool _isDebugModeOpened;
    private bool _isOpened;
    private bool _layoutConfigured;
    private bool _dismissed;
    private PixelPoint _targetPosition;
    private PixelPoint _slideHiddenPosition;

    public SplashWindow()
        : this(StartupVisualMode.Fade)
    {
    }

    public SplashWindow(StartupVisualMode mode)
    {
        _mode = mode;
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
        ConfigureForVisualMode();

        if (_mode == StartupVisualMode.Fade)
        {
            Opacity = 0d;
            await AnimateOpacityAsync(0d, 1d, FadeAnimationDuration).ConfigureAwait(false);
            return;
        }

        Opacity = 1d;
        if (_mode == StartupVisualMode.SlideSplash)
        {
            await AnimateWindowPositionAsync(_slideHiddenPosition, _targetPosition, SlideAnimationDuration, EaseOutCubic).ConfigureAwait(false);
        }
    }

    public async Task DismissAsync()
    {
        if (_dismissed)
        {
            return;
        }

        _dismissed = true;

        // 确保在UI线程上执行
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(async () => await DismissAsync());
            return;
        }

        ConfigureForVisualMode();

        if (_mode == StartupVisualMode.SlideSplash)
        {
            var from = Position;
            await AnimateWindowPositionAsync(from, _slideHiddenPosition, SlideAnimationDuration, EaseInCubic).ConfigureAwait(false);
        }
        else if (_mode == StartupVisualMode.Fade)
        {
            await AnimateOpacityAsync(Opacity, 0d, FadeAnimationDuration).ConfigureAwait(false);
        }

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

    private void ConfigureForVisualMode()
    {
        if (_layoutConfigured)
        {
            return;
        }

        _layoutConfigured = true;
        var compactHero = this.FindControl<Grid>("CompactHero");
        var fullscreenHero = this.FindControl<Grid>("FullscreenHero");

        if (_mode == StartupVisualMode.Fade)
        {
            compactHero?.SetCurrentValue(IsVisibleProperty, true);
            fullscreenHero?.SetCurrentValue(IsVisibleProperty, false);
            Background = new SolidColorBrush(Color.Parse("#0B0B0B"));
            Width = 480;
            Height = 320;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        compactHero?.SetCurrentValue(IsVisibleProperty, false);
        fullscreenHero?.SetCurrentValue(IsVisibleProperty, true);
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var screen = Screens?.Primary ?? Screens?.All.FirstOrDefault();
        var workingArea = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = Math.Max(screen?.Scaling ?? 1d, 0.01d);

        Width = workingArea.Width / scale;
        Height = workingArea.Height / scale;
        _targetPosition = new PixelPoint(workingArea.X, workingArea.Y);
        _slideHiddenPosition = new PixelPoint(workingArea.X + workingArea.Width, workingArea.Y);
        Position = _mode == StartupVisualMode.SlideSplash
            ? _slideHiddenPosition
            : _targetPosition;
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

    private async Task AnimateWindowPositionAsync(
        PixelPoint from,
        PixelPoint to,
        TimeSpan duration,
        Func<double, double> easing)
    {
        await AnimateAsync(progress =>
        {
            var currentX = (int)Math.Round(from.X + ((to.X - from.X) * progress));
            var currentY = (int)Math.Round(from.Y + ((to.Y - from.Y) * progress));
            Position = new PixelPoint(currentX, currentY);
        }, duration, easing).ConfigureAwait(false);
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

    private static double EaseInCubic(double value) => value * value * value;
}
