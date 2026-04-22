using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace LanMountainDesktop.Launcher.Views;

public partial class OobeWindow : Window
{
    private readonly TaskCompletionSource<bool> _completionSource = new();
    private bool _isTransitioning;

    public OobeWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnWindowLoaded;
        Opened += OnWindowOpened;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("[OobeWindow] Window loaded, initializing components...");

        var enterButton = this.FindControl<Button>("EnterButton");
        if (enterButton is not null)
        {
            enterButton.Click += OnEnterClick;
            Console.WriteLine("[OobeWindow] EnterButton event bound successfully");
        }
        else
        {
            Console.Error.WriteLine("[OobeWindow] Failed to find EnterButton!");
        }
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        Console.WriteLine("[OobeWindow] Window opened, playing entrance animation...");
        await PlayEntranceAnimationAsync();
    }

    private async Task PlayEntranceAnimationAsync()
    {
        try
        {
            var contentGrid = this.FindControl<Grid>("ContentGrid");
            if (contentGrid is null)
            {
                return;
            }

            var translateTransform = contentGrid.RenderTransform as TranslateTransform ?? new TranslateTransform();
            contentGrid.RenderTransform = translateTransform;

            var offset = ResolveEntranceOffset();
            contentGrid.Opacity = 0;
            translateTransform.Y = offset;

            var fadeInAnimation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(600),
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Setters = { new Setter(OpacityProperty, 0.0) },
                        KeyTime = TimeSpan.FromMilliseconds(0)
                    },
                    new KeyFrame
                    {
                        Setters = { new Setter(OpacityProperty, 1.0) },
                        KeyTime = TimeSpan.FromMilliseconds(600)
                    }
                }
            };

            var slideUpAnimation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(600),
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Setters = { new Setter(TranslateTransform.YProperty, offset) },
                        KeyTime = TimeSpan.FromMilliseconds(0)
                    },
                    new KeyFrame
                    {
                        Setters = { new Setter(TranslateTransform.YProperty, 0.0) },
                        KeyTime = TimeSpan.FromMilliseconds(600)
                    }
                }
            };

            await Task.WhenAll(
                fadeInAnimation.RunAsync(contentGrid),
                slideUpAnimation.RunAsync(translateTransform));

            Console.WriteLine("[OobeWindow] Entrance animation completed");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OobeWindow] Error playing entrance animation: {ex.Message}");
        }
    }

    public Task WaitForEnterAsync() => _completionSource.Task;

    private async void OnEnterClick(object? sender, RoutedEventArgs e)
    {
        if (_isTransitioning)
        {
            return;
        }

        _isTransitioning = true;
        Console.WriteLine("[OobeWindow] Enter button clicked, starting transition...");

        try
        {
            await PlayExitAnimationAsync();
            _completionSource.TrySetResult(true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OobeWindow] Error during transition: {ex.Message}");
            _completionSource.TrySetResult(true);
        }
    }

    private async Task PlayExitAnimationAsync()
    {
        try
        {
            var contentGrid = this.FindControl<Grid>("ContentGrid");
            if (contentGrid is null)
            {
                await Task.Delay(200);
                return;
            }

            var fadeOutAnimation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new CubicEaseIn(),
                Children =
                {
                    new KeyFrame
                    {
                        Setters = { new Setter(OpacityProperty, 1.0) },
                        KeyTime = TimeSpan.FromMilliseconds(0)
                    },
                    new KeyFrame
                    {
                        Setters = { new Setter(OpacityProperty, 0.0) },
                        KeyTime = TimeSpan.FromMilliseconds(200)
                    }
                }
            };

            await fadeOutAnimation.RunAsync(contentGrid);
            Console.WriteLine("[OobeWindow] Exit animation completed");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OobeWindow] Error playing exit animation: {ex.Message}");
        }
    }

    private double ResolveEntranceOffset()
    {
        var boundsHeight = Bounds.Height > 0 ? Bounds.Height : Height;
        var scaledOffset = boundsHeight * 0.05;
        return Math.Clamp(scaledOffset, 20, 48);
    }
}
