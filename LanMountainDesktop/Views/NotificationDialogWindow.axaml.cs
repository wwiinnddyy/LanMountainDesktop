using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentIcons.Avalonia;
using FluentIcons.Avalonia.Fluent;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class NotificationDialogWindow : Window
{
    private NotificationDialogViewModel? _viewModel;
    private DispatcherTimer? _autoCloseTimer;
    private bool _isClosing;

    public TaskCompletionSource<bool>? CompletionSource { get; private set; }

    public NotificationDialogWindow()
    {
        InitializeComponent();
    }

    public void Initialize(NotificationContent content, IAppearanceThemeService? themeService = null)
    {
        _viewModel = new NotificationDialogViewModel(content, this);
        DataContext = _viewModel;

        CompletionSource = new TaskCompletionSource<bool>();

        bool isNightMode = false;
        if (themeService is not null)
        {
            var snapshot = themeService.GetCurrent();
            isNightMode = snapshot.IsNightMode;
            RequestedThemeVariant = isNightMode ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        if (DialogCard is not null)
        {
            DialogCard.Background = isNightMode
                ? new SolidColorBrush(Color.Parse("#FF2D2D2D"))
                : new SolidColorBrush(Color.Parse("#FFF8F9FA"));
        }

        if (!HasButtons(content) && content.Duration.HasValue)
        {
            _autoCloseTimer = new DispatcherTimer
            {
                Interval = content.Duration.Value
            };
            _autoCloseTimer.Tick += OnAutoCloseTimerTick;
            _autoCloseTimer.Start();
        }
    }

    private static bool HasButtons(NotificationContent content)
    {
        return !string.IsNullOrEmpty(content.PrimaryButtonText) ||
               !string.IsNullOrEmpty(content.SecondaryButtonText);
    }

    private void OnAutoCloseTimerTick(object? sender, EventArgs e)
    {
        _autoCloseTimer?.Stop();
        _ = CloseWithResultAsync(false);
    }

    public void OnPrimaryButtonClick()
    {
        _ = CloseWithResultAsync(true);
    }

    public void OnSecondaryButtonClick()
    {
        _ = CloseWithResultAsync(false);
    }

    private async Task CloseWithResultAsync(bool result)
    {
        if (_isClosing) return;
        _isClosing = true;

        _autoCloseTimer?.Stop();

        if (DialogCard is not null)
        {
            DialogCard.RenderTransform = new ScaleTransform(1, 1);
            DialogCard.Opacity = 1;

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

            await animation.RunAsync(DialogCard);
        }

        CompletionSource?.TrySetResult(result);
        Close();
    }
}

public partial class NotificationDialogViewModel : ObservableObject
{
    private readonly NotificationDialogWindow _window;
    private readonly NotificationContent _content;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string? _primaryButtonText;
    [ObservableProperty] private string? _secondaryButtonText;
    [ObservableProperty] private bool _hasButtons;
    [ObservableProperty] private string _severityIcon = "Info";
    [ObservableProperty] private IBrush? _severityBackground;

    public NotificationDialogViewModel(NotificationContent content, NotificationDialogWindow window)
    {
        _window = window;
        _content = content;

        Title = content.Title;
        Message = content.Message;
        PrimaryButtonText = content.PrimaryButtonText;
        SecondaryButtonText = content.SecondaryButtonText;
        HasButtons = !string.IsNullOrEmpty(content.PrimaryButtonText) || 
                     !string.IsNullOrEmpty(content.SecondaryButtonText);

        (SeverityIcon, SeverityBackground) = content.Severity switch
        {
            NotificationSeverity.Success => ("CheckmarkCircle", new SolidColorBrush(Color.Parse("#FF10B981"))),
            NotificationSeverity.Warning => ("Warning", new SolidColorBrush(Color.Parse("#FFF59E0B"))),
            NotificationSeverity.Error => ("ErrorCircle", new SolidColorBrush(Color.Parse("#FFEF4444"))),
            _ => ("Info", new SolidColorBrush(Color.Parse("#FF3B82F6")))
        };
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Primary()
    {
        _content.OnPrimaryButtonClick?.Invoke();
        _window.OnPrimaryButtonClick();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Secondary()
    {
        _content.OnSecondaryButtonClick?.Invoke();
        _window.OnSecondaryButtonClick();
    }
}
