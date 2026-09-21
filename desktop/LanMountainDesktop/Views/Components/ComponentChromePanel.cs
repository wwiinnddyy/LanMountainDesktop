using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件外框的"玻璃面板"外壳开关。这段序列此前在 3 个组件里逐字抄了 3 份：
/// 透明档要把样式类摘掉并把四个属性显式置空，非透明档要把类挂回去并清掉本地值让样式接管。
/// 两边不对称写就会留下"关了透明还留着阴影"这类残留视觉。
/// </summary>
public static class ComponentChromePanel
{
    /// <summary>样式里的玻璃面板类名。.axaml 侧的选择器仍是字面量，C# 这一侧只认这个常量。</summary>
    public const string GlassPanelClass = "glass-panel";

    public static void Apply(Border root, bool transparent)
    {
        if (transparent)
        {
            root.Classes.Remove(GlassPanelClass);
            root.Background = Brushes.Transparent;
            root.BorderBrush = Brushes.Transparent;
            root.BorderThickness = new Thickness(0);
            root.BoxShadow = default;
            return;
        }

        if (!root.Classes.Contains(GlassPanelClass))
        {
            root.Classes.Add(GlassPanelClass);
        }

        root.ClearValue(Border.BackgroundProperty);
        root.ClearValue(Border.BorderBrushProperty);
        root.ClearValue(Border.BorderThicknessProperty);
        root.ClearValue(Border.BoxShadowProperty);
    }
}
