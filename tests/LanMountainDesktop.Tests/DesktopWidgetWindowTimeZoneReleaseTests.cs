using System.Reflection;
using Avalonia.Headless.XUnit;
using LanMountainDesktop.Services;
using LanMountainDesktop.Views;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件窗口（桌面管理视图给每个组件开的浮窗）关掉时，OnClosing 原来只退自己的事件与 Dispose，
/// 漏了退掉对应用级 <see cref="TimeZoneService"/> 的订阅——窗口可以反复开合，
/// 服务就把每一任控件树都留在手里。判据同预览那条：数服务侧的订阅者个数。
/// </summary>
public sealed class DesktopWidgetWindowTimeZoneReleaseTests
{
    [AvaloniaFact]
    public void ClosingTheWidgetWindow_ReleasesTheServiceReferenceToTheComponent()
    {
        var service = new TimeZoneService();
        var widget = new ClockWidget();
        widget.SetTimeZoneService(service);

        Assert.Equal(1, SubscriberCount(service));

        var window = new DesktopWidgetWindow(widget, "placement", 18d);
        window.Close();

        Assert.Equal(0, SubscriberCount(service));
    }

    private static int SubscriberCount(TimeZoneService service)
    {
        var field = typeof(TimeZoneService)
            .GetField("TimeZoneChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(service) is MulticastDelegate handler
            ? handler.GetInvocationList().Length
            : 0;
    }
}
