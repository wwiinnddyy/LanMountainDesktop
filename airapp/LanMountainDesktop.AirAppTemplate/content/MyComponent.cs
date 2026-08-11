using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.AirAppTemplate;

/// <summary>
/// 桌面组件控件。主进程内渲染，构造函数可接收 <see cref="AirAppComponentContext"/>。
/// </summary>
public sealed class MyComponent : UserControl
{
    private readonly TextBlock _text = new()
    {
        FontSize = 18,
        FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    public MyComponent(AirAppComponentContext context)
    {
        _text.Text = $"Hello AirApp: {context.ComponentId}";
        Content = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _text
            }
        };
    }
}
