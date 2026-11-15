using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using TKCode.Arc.Icons.Lucide;

namespace LanMountainDesktop.Controls;

public partial class IconText : UserControl
{
    // D2 图标迁移：页面继续传 FluentIcons 枚举名（属性保持 string，页面 XAML 零改动），
    // 内部解析到 Lucide 图标；名字相同的 Enum.TryParse 直接命中，语义相近的按下表换名。
    private static readonly Dictionary<string, LucideIconKind> IconNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alert"] = LucideIconKind.TriangleAlert,
        ["Apps"] = LucideIconKind.LayoutGrid,
        ["Beaker"] = LucideIconKind.FlaskConical,
        ["Color"] = LucideIconKind.Palette,
        ["DeveloperBoard"] = LucideIconKind.Cpu,
        ["PuzzleCube"] = LucideIconKind.Puzzle,
        ["ShapeOrganic"] = LucideIconKind.Shapes,
    };

    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<IconText, string>(nameof(Icon), "Info");

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<IconText, string>(nameof(Text), string.Empty);

    public IconText()
    {
        InitializeComponent();
    }

    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconProperty)
        {
            if (IconElement is not null && TryResolveKind(change.GetNewValue<string>(), out var kind))
            {
                IconElement.Kind = kind;
            }
        }
        else if (change.Property == TextProperty)
        {
            if (TextElement is not null)
            {
                TextElement.Text = change.GetNewValue<string?>();
            }
        }
    }

    private static bool TryResolveKind(string? name, out LucideIconKind kind)
    {
        if (Enum.TryParse(name, ignoreCase: true, out kind))
        {
            return true;
        }

        return name is not null && IconNameMap.TryGetValue(name, out kind);
    }
}
