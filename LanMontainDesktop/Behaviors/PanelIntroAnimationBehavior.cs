using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace LanMontainDesktop.Behaviors;

public class PanelIntroAnimationBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<PanelIntroAnimationBehavior, Panel, bool>("IsEnabled");

    public static readonly AttachedProperty<bool> IsAnimationPlayedProperty =
        AvaloniaProperty.RegisterAttached<PanelIntroAnimationBehavior, Control, bool>("IsAnimationPlayed");

    public static readonly AttachedProperty<bool> CanPlayAnimationProperty =
        AvaloniaProperty.RegisterAttached<PanelIntroAnimationBehavior, Control, bool>("CanPlayAnimation");

    private static readonly AttachedProperty<bool> IsAnimationStartedProperty =
        AvaloniaProperty.RegisterAttached<PanelIntroAnimationBehavior, Panel, bool>("IsAnimationStarted");

    static PanelIntroAnimationBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Panel>(OnIsEnabledChanged);
    }

    public static void SetIsEnabled(Panel panel, bool value)
    {
        panel.SetValue(IsEnabledProperty, value);
    }

    public static bool GetIsEnabled(Panel panel)
    {
        return panel.GetValue(IsEnabledProperty);
    }

    public static void SetIsAnimationPlayed(Control control, bool value)
    {
        control.SetValue(IsAnimationPlayedProperty, value);
    }

    public static bool GetIsAnimationPlayed(Control control)
    {
        return control.GetValue(IsAnimationPlayedProperty);
    }

    public static void SetCanPlayAnimation(Control control, bool value)
    {
        control.SetValue(CanPlayAnimationProperty, value);
    }

    public static bool GetCanPlayAnimation(Control control)
    {
        return control.GetValue(CanPlayAnimationProperty);
    }

    private static bool GetIsAnimationStarted(Panel panel)
    {
        return panel.GetValue(IsAnimationStartedProperty);
    }

    private static void SetIsAnimationStarted(Panel panel, bool value)
    {
        panel.SetValue(IsAnimationStartedProperty, value);
    }

    private static void OnIsEnabledChanged(Panel panel, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is not bool isEnabled || !isEnabled || GetIsAnimationStarted(panel))
        {
            return;
        }

        panel.AttachedToVisualTree += OnPanelAttachedToVisualTree;
        StartStaggerAnimation(panel);
    }

    private static void OnPanelAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Panel panel)
        {
            return;
        }

        StartStaggerAnimation(panel);
    }

    private static void StartStaggerAnimation(Panel panel)
    {
        if (!GetIsEnabled(panel) || GetIsAnimationStarted(panel))
        {
            return;
        }

        var targets = panel.Children
            .OfType<Control>()
            .Where(control => control.IsVisible)
            .ToList();

        foreach (var target in targets)
        {
            SetCanPlayAnimation(target, true);
            SetIsAnimationPlayed(target, false);
        }

        SetIsAnimationStarted(panel, true);

        var index = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(24)
        };
        timer.Tick += (_, _) =>
        {
            if (index >= targets.Count)
            {
                timer.Stop();
                return;
            }

            SetIsAnimationPlayed(targets[index], true);
            index++;
        };
        timer.Start();
    }
}
