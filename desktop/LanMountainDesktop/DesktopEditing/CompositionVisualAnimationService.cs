using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;

namespace LanMountainDesktop.DesktopEditing;

internal sealed class CompositionVisualAnimationService
{
    private readonly Func<Visual, CompositionVisual?> _getVisual;

    public CompositionVisualAnimationService()
        : this(ElementComposition.GetElementVisual)
    {
    }

    internal CompositionVisualAnimationService(Func<Visual, CompositionVisual?> getVisual)
    {
        _getVisual = getVisual;
    }

    public bool TrySetOffset(Control target, Point offset)
    {
        return TryApply(target, visual =>
        {
            visual.StopAnimation(nameof(visual.Offset));
            visual.Offset = visual.Offset with
            {
                X = offset.X,
                Y = offset.Y
            };
        });
    }

    public bool TrySetOpacity(Control target, double opacity)
    {
        return TryApply(target, visual =>
        {
            visual.StopAnimation(nameof(visual.Opacity));
            visual.Opacity = (float)Math.Clamp(opacity, 0, 1);
        });
    }

    public bool TrySetUniformScale(Control target, double scale)
    {
        return TryApply(target, visual =>
        {
            var clampedScale = Math.Clamp(scale, 0.01, 64);
            visual.StopAnimation(nameof(visual.Scale));
            visual.Scale = visual.Scale with
            {
                X = clampedScale,
                Y = clampedScale,
                Z = 1
            };
        });
    }

    public bool TryResetOffset(Control target)
    {
        return TrySetOffset(target, new Point());
    }

    private bool TryApply(Control target, Action<CompositionVisual> apply)
    {
        try
        {
            var visual = _getVisual(target);
            if (visual is null)
            {
                return false;
            }

            apply(visual);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
