using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace LanMountainDesktop.Launcher.Views;

/// <summary>
/// OOBE（首次使用体验）窗口 - 欢迎页面
/// </summary>
public partial class OobeWindow : Window
{
    private readonly TaskCompletionSource<bool> _completionSource = new();
    private bool _isTransitioning = false;

    public OobeWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // 延迟到窗口加载完成后再初始化
        this.Loaded += OnWindowLoaded;
        this.Opened += OnWindowOpened;
    }

    /// <summary>
    /// 窗口加载完成事件
    /// </summary>
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

    /// <summary>
    /// 窗口打开事件 - 播放入场动画
    /// </summary>
    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        Console.WriteLine("[OobeWindow] Window opened, playing entrance animation...");
        await PlayEntranceAnimationAsync();
    }

    /// <summary>
    /// 播放入场动画
    /// </summary>
    private async Task PlayEntranceAnimationAsync()
    {
        try
        {
            // 获取内容元素
            var contentGrid = this.FindControl<Grid>("ContentGrid");
            if (contentGrid is null)
            {
                // 如果没有命名网格，直接返回
                return;
            }

            // 创建淡入动画
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

            // 创建向上滑动动画
            var slideUpAnimation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(600),
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Setters = { new Setter(TranslateTransform.YProperty, 30.0) },
                        KeyTime = TimeSpan.FromMilliseconds(0)
                    },
                    new KeyFrame
                    {
                        Setters = { new Setter(TranslateTransform.YProperty, 0.0) },
                        KeyTime = TimeSpan.FromMilliseconds(600)
                    }
                }
            };

            // 应用动画
            await fadeInAnimation.RunAsync(contentGrid);
            await slideUpAnimation.RunAsync(contentGrid);

            Console.WriteLine("[OobeWindow] Entrance animation completed");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OobeWindow] Error playing entrance animation: {ex.Message}");
        }
    }

    /// <summary>
    /// 等待用户点击开始按钮
    /// </summary>
    public Task WaitForEnterAsync() => _completionSource.Task;

    /// <summary>
    /// 进入按钮点击事件
    /// </summary>
    private async void OnEnterClick(object? sender, RoutedEventArgs e)
    {
        if (_isTransitioning) return;
        _isTransitioning = true;

        Console.WriteLine("[OobeWindow] Enter button clicked, starting transition...");

        try
        {
            // 播放退出动画
            await PlayExitAnimationAsync();

            // 完成 OOBE
            _completionSource.TrySetResult(true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OobeWindow] Error during transition: {ex.Message}");
            _completionSource.TrySetResult(true);
        }
    }

    /// <summary>
    /// 播放退出动画
    /// </summary>
    private async Task PlayExitAnimationAsync()
    {
        try
        {
            var contentGrid = this.FindControl<Grid>("ContentGrid");
            if (contentGrid is null)
            {
                // 如果没有命名网格，直接延迟后返回
                await Task.Delay(200);
                return;
            }

            // 创建淡出动画
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
}
