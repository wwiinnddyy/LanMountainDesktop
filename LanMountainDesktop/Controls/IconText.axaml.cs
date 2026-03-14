using Avalonia;
using Avalonia.Controls;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace LanMountainDesktop.Controls;

public partial class IconText : UserControl
{
    public static readonly StyledProperty<Icon> IconProperty =
        AvaloniaProperty.Register<IconText, Icon>(nameof(Icon), Icon.Info);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<IconText, string>(nameof(Text), string.Empty);

    public IconText()
    {
        InitializeComponent();
    }

    public Icon Icon
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
            if (IconElement is not null)
            {
                IconElement.Icon = change.GetNewValue<Icon>();
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
}
