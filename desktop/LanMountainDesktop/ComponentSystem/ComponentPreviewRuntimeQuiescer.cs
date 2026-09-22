using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.ComponentSystem;

internal static class ComponentPreviewRuntimeQuiescer
{
    private static readonly BindingFlags TimerMemberFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Attach(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        control.IsHitTestVisible = false;
        control.Focusable = false;
        control.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(() => Quiesce(control), DispatcherPriority.Background);
        control.DetachedFromVisualTree += (_, _) =>
        {
            Quiesce(control);
            ReleaseTimeZoneBindings(control);
        };
        Quiesce(control);
    }

    /// <summary>预览控件被丢掉时调这里：停表之外还要退掉服务订阅，否则应用级的 TimeZoneService 会一直替它持有这棵已分离的 visual tree。</summary>
    public static void Detach(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        Quiesce(control);
        ReleaseTimeZoneBindings(control);
    }

    public static void Quiesce(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        foreach (var candidate in EnumerateControls(control))
        {
            StopDispatcherTimers(candidate);
            candidate.IsHitTestVisible = false;
            candidate.Focusable = false;
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;

        foreach (var descendant in root.GetVisualDescendants().OfType<Control>())
        {
            yield return descendant;
        }
    }

    private static void ReleaseTimeZoneBindings(Control root)
    {
        foreach (var candidate in EnumerateControls(root))
        {
            (candidate as ITimeZoneAwareComponentWidget)?.ClearTimeZoneService();
        }
    }

    private static void StopDispatcherTimers(object target)
    {
        var type = target.GetType();
        foreach (var field in type.GetFields(TimerMemberFlags))
        {
            if (typeof(DispatcherTimer).IsAssignableFrom(field.FieldType) &&
                field.GetValue(target) is DispatcherTimer timer)
            {
                timer.Stop();
            }
        }

        foreach (var property in type.GetProperties(TimerMemberFlags))
        {
            if (!property.CanRead ||
                property.GetIndexParameters().Length != 0 ||
                !typeof(DispatcherTimer).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            try
            {
                if (property.GetValue(target) is DispatcherTimer timer)
                {
                    timer.Stop();
                }
            }
            catch (TargetInvocationException)
            {
            }
        }
    }
}
