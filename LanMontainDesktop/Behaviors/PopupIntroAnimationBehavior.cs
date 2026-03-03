using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Rendering.Composition;

namespace LanMontainDesktop.Behaviors;

public class PopupIntroAnimationBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<PopupIntroAnimationBehavior, Control, bool>("IsEnabled");

    private static readonly AttachedProperty<bool> IsHookedProperty =
        AvaloniaProperty.RegisterAttached<PopupIntroAnimationBehavior, Control, bool>("IsHooked");

    static PopupIntroAnimationBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    public static void SetIsEnabled(Control control, bool value)
    {
        control.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(Control control)
    {
        return control.GetValue(IsEnabledProperty);
    }

    private static bool GetIsHooked(Control control)
    {
        return control.GetValue(IsHookedProperty);
    }

    private static void SetIsHooked(Control control, bool value)
    {
        control.SetValue(IsHookedProperty, value);
    }

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is not bool isEnabled || !isEnabled || GetIsHooked(control))
        {
            return;
        }

        switch (control)
        {
            case PopupRoot popupRoot:
                popupRoot.Opened += OnPopupOpened;
                SetIsHooked(popupRoot, true);
                break;
            case OverlayPopupHost overlayPopupHost:
                overlayPopupHost.AttachedToVisualTree += OnOverlayPopupHostAttached;
                SetIsHooked(overlayPopupHost, true);
                break;
        }
    }

    private static void OnPopupOpened(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            PlayIntroAnimation(control);
        }
    }

    private static void OnOverlayPopupHostAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
        {
            PlayIntroAnimation(control);
        }
    }

    private static void PlayIntroAnimation(Control control)
    {
        var compositionVisual = ElementComposition.GetElementVisual(control);
        if (compositionVisual is null)
        {
            return;
        }

        var popup = control.Parent as Popup;
        compositionVisual.CenterPoint = ResolveCenterPoint(
            popup?.Placement ?? PlacementMode.Pointer,
            control.Bounds.Size,
            compositionVisual.CenterPoint);

        var compositor = compositionVisual.Compositor;

        var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.Target = nameof(compositionVisual.Opacity);
        opacityAnimation.Duration = TimeSpan.FromMilliseconds(160);
        opacityAnimation.InsertKeyFrame(0f, 0f);
        opacityAnimation.InsertKeyFrame(1f, 1f, Easing.Parse("0.22, 1, 0.36, 1"));
        compositionVisual.StartAnimation(nameof(compositionVisual.Opacity), opacityAnimation);

        var scaleAnimation = compositor.CreateVector3DKeyFrameAnimation();
        scaleAnimation.Target = nameof(compositionVisual.Scale);
        scaleAnimation.Duration = TimeSpan.FromMilliseconds(160);
        scaleAnimation.InsertKeyFrame(0f, compositionVisual.Scale with { X = 0.94, Y = 0.94 });
        scaleAnimation.InsertKeyFrame(1f, compositionVisual.Scale with { X = 1, Y = 1 }, Easing.Parse("0.22, 1, 0.36, 1"));
        compositionVisual.StartAnimation(nameof(compositionVisual.Scale), scaleAnimation);
    }

    private static Vector3D ResolveCenterPoint(PlacementMode placement, Size size, Vector3D fallback)
    {
        var relative = placement switch
        {
            PlacementMode.Bottom => new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
            PlacementMode.Left => new RelativePoint(1.0, 0.5, RelativeUnit.Relative),
            PlacementMode.Right => new RelativePoint(0.0, 0.5, RelativeUnit.Relative),
            PlacementMode.Top => new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
            PlacementMode.Pointer => new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
            PlacementMode.BottomEdgeAlignedLeft => new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
            PlacementMode.BottomEdgeAlignedRight => new RelativePoint(1.0, 0.0, RelativeUnit.Relative),
            PlacementMode.LeftEdgeAlignedTop => new RelativePoint(1.0, 1.0, RelativeUnit.Relative),
            PlacementMode.LeftEdgeAlignedBottom => new RelativePoint(1.0, 0.0, RelativeUnit.Relative),
            PlacementMode.RightEdgeAlignedTop => new RelativePoint(0.0, 1.0, RelativeUnit.Relative),
            PlacementMode.RightEdgeAlignedBottom => new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
            PlacementMode.TopEdgeAlignedLeft => new RelativePoint(0.0, 1.0, RelativeUnit.Relative),
            PlacementMode.TopEdgeAlignedRight => new RelativePoint(1.0, 1.0, RelativeUnit.Relative),
            _ => new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        };

        return fallback with
        {
            X = size.Width * relative.Point.X,
            Y = size.Height * relative.Point.Y
        };
    }
}
