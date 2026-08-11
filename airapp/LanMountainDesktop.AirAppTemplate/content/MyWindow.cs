using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.AirAppTemplate;

/// <summary>
/// 窗口轻应用。内容由 AirAppHost 独立进程承载，复用宿主 FAAppWindow 外壳。
/// </summary>
public sealed class MyWindow : AirAppWindowBase
{
    public MyWindow()
    {
        Content = new TextBlock
        {
            Text = "My AirApp Window",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
