using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Behaviors;

public static class WindowSlideAnimationBehavior
{
    private static readonly Easing DecelerateEasing = Easing.Parse(FluttermotionToken.StandardBezier);
    private static readonly Easing AccelerateEasing = new CubicEaseIn();

    public static readonly TimeSpan SlideInDuration = TimeSpan.FromMilliseconds(350);
    public static readonly TimeSpan SlideOutDuration = TimeSpan.FromMilliseconds(280);

    public static async Task SlideInAsync(Window window, Border desktopHost)
    {
        if (window is null || desktopHost is null)
        {
            return;
        }

        var screenWidth = Math.Max(1, window.Bounds.Width > 1 ? window.Bounds.Width : PrimaryScreenWidth(window));
        var transform = EnsureTranslateTransform(desktopHost);

        transform.X = screenWidth;
        desktopHost.Opacity = 1;
        window.Show();

        if (screenWidth <= 1)
        {
            transform.X = 0;
            return;
        }

        var animation = new Animation
        {
            Duration = SlideInDuration,
            Easing = DecelerateEasing,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(TranslateTransform.XProperty, screenWidth) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(TranslateTransform.XProperty, 0d) }
                }
            }
        };

        await animation.RunAsync(desktopHost);
    }

    public static async Task SlideOutAsync(Window window, Border desktopHost, Action? onCompleted = null)
    {
        if (window is null || desktopHost is null)
        {
            onCompleted?.Invoke();
            return;
        }

        var screenWidth = Math.Max(1, window.Bounds.Width > 1 ? window.Bounds.Width : PrimaryScreenWidth(window));
        var transform = EnsureTranslateTransform(desktopHost);

        if (screenWidth <= 1)
        {
            onCompleted?.Invoke();
            return;
        }

        var animation = new Animation
        {
            Duration = SlideOutDuration,
            Easing = AccelerateEasing,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(TranslateTransform.XProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(TranslateTransform.XProperty, screenWidth) }
                }
            }
        };

        await animation.RunAsync(desktopHost);
        onCompleted?.Invoke();
    }

    public static void ResetSlidePosition(Border desktopHost)
    {
        if (desktopHost is null)
        {
            return;
        }

        var transform = desktopHost.RenderTransform as TranslateTransform;
        if (transform is not null)
        {
            transform.X = 0;
        }

        desktopHost.Opacity = 1;
    }

    private static TranslateTransform EnsureTranslateTransform(Border desktopHost)
    {
        if (desktopHost.RenderTransform is TranslateTransform existingTransform)
        {
            return existingTransform;
        }

        var newTransform = new TranslateTransform();
        desktopHost.RenderTransform = newTransform;
        return newTransform;
    }

    private static double PrimaryScreenWidth(Window window)
    {
        try
        {
            if (window.Screens?.Primary is { } screen)
            {
                return screen.WorkingArea.Width;
            }
        }
        catch
        {
        }

        return 1920;
    }
}
