using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views;

public partial class NotificationWindow : Window
{
    private NotificationViewModel? _viewModel;
    private DispatcherTimer? _autoCloseTimer;
    private bool _isClosing;
    private TimeSpan _remainingDuration;

    public Guid NotificationId => _viewModel?.Id ?? Guid.Empty;
    public NotificationPosition NotificationPositionValue => _viewModel?.Position ?? NotificationPosition.TopRight;

    public NotificationWindow()
    {
        InitializeComponent();
        _remainingDuration = TimeSpan.FromSeconds(4);
    }

    public void Initialize(NotificationViewModel viewModel, IAppearanceThemeService? themeService = null)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        
        _remainingDuration = viewModel.Duration;
        
        ApplyTheme(themeService);
        ApplySeverityColor();
    }

    private void ApplyTheme(IAppearanceThemeService? themeService)
    {
        if (themeService is null) return;

        var snapshot = themeService.GetCurrent();
        RequestedThemeVariant = snapshot.IsNightMode ? ThemeVariant.Dark : ThemeVariant.Light;

        // Apply glass effect resources directly to window resources
        // This ensures the notification card has proper background/border colors
        var context = CreateThemeContext(snapshot);
        GlassEffectService.ApplyGlassResources(Resources, context);

        // IMPORTANT: Do NOT call ApplyWindowMaterial for notification windows!
        // ApplyWindowMaterial sets Background to White when MaterialMode is "None",
        // which causes the white border around the notification card.
        // Notification windows must always have transparent background.
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
    }

    private ThemeColorContext CreateThemeContext(AppearanceThemeSnapshot snapshot)
    {
        // Create theme context for glass effect resources
        // Note: IsLightBackground and IsLightNavBackground are derived from IsNightMode
        // UseNeutralSurfaces is determined by ThemeColorMode
        var useNeutralSurfaces = snapshot.ThemeColorMode == "Neutral";
        var monetColors = snapshot.WallpaperSeedCandidates;

        return new ThemeColorContext(
            AccentColor: snapshot.AccentColor,
            IsLightBackground: !snapshot.IsNightMode,
            IsLightNavBackground: !snapshot.IsNightMode,
            IsNightMode: snapshot.IsNightMode,
            MonetPalette: snapshot.MonetPalette,
            MonetColors: monetColors,
            UseNeutralSurfaces: useNeutralSurfaces,
            SystemMaterialMode: snapshot.SystemMaterialMode);
    }

    private void ApplySeverityColor()
    {
        if (_viewModel is null) return;
        
        if (this.TryFindResource(_viewModel.SeverityColorResource, out var resource) && resource is IBrush brush)
        {
            SeverityIndicator.Background = brush;
        }
        else
        {
            // Fallback for custom theme compatibility
            var severityColor = _viewModel.Severity switch
            {
                NotificationSeverity.Success => Color.Parse("#FF10B981"),
                NotificationSeverity.Warning => Color.Parse("#FFF59E0B"),
                NotificationSeverity.Error => Color.Parse("#FFEF4444"),
                _ => Color.Parse("#FF3B82F6")
            };
            SeverityIndicator.Background = new SolidColorBrush(severityColor);
        }
    }

    public void StartAutoCloseTimer()
    {
        _autoCloseTimer = new DispatcherTimer
        {
            Interval = _remainingDuration
        };
        _autoCloseTimer.Tick += OnAutoCloseTimerTick;
        _autoCloseTimer.Start();
    }

    private void OnAutoCloseTimerTick(object? sender, EventArgs e)
    {
        _autoCloseTimer?.Stop();
        Dispatcher.UIThread.Post(() => _ = CloseWithAnimationAsync());
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel?.OnClick is not null)
        {
            _viewModel.OnClick.Invoke();
        }
        _ = CloseWithAnimationAsync();
    }

    private void OnCardPointerEntered(object? sender, PointerEventArgs e)
    {
        _autoCloseTimer?.Stop();
        CardBorder.Opacity = 0.95;
    }

    private void OnCardPointerExited(object? sender, PointerEventArgs e)
    {
        CardBorder.Opacity = 1;
        StartAutoCloseTimer();
    }

    public async Task CloseWithAnimationAsync()
    {
        if (_isClosing) return;
        _isClosing = true;

        _autoCloseTimer?.Stop();
        
        if (_viewModel is not null)
        {
            _viewModel.IsClosing = true;
        }

        CardBorder.RenderTransform = new ScaleTransform(1, 1);
        CardBorder.Opacity = 1;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new QuadraticEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1d),
                        new Setter(ScaleTransform.ScaleYProperty, 1d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.9d),
                        new Setter(ScaleTransform.ScaleYProperty, 0.9d)
                    }
                }
            }
        };

        await animation.RunAsync(CardBorder);

        Close();
    }

    public async Task ShowWithAnimationAsync()
    {
        // Show window first (material should already be applied in Initialize)
        Show();

        // Ensure render transform is set before animation
        CardBorder.RenderTransform = new ScaleTransform(0.85, 0.85);
        CardBorder.Opacity = 0;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(250),
            Easing = new QuadraticEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.85d),
                        new Setter(ScaleTransform.ScaleYProperty, 0.85d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1d),
                        new Setter(ScaleTransform.ScaleYProperty, 1d)
                    }
                }
            }
        };

        await animation.RunAsync(CardBorder);
        
        StartAutoCloseTimer();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _autoCloseTimer?.Stop();
        base.OnClosing(e);
    }
}
