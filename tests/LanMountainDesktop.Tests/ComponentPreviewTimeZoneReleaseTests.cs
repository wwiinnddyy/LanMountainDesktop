using System.Reflection;
using Avalonia.Headless.XUnit;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件库预览每换一次选中项就造一个控件、丢掉时只停了计时器不退订，
/// 而 <see cref="TimeZoneService"/> 是应用级长命对象——服务就替那些已经分离的控件一直持有整棵 visual tree。
/// 这里数的是服务侧的订阅者个数：0 → 1 → 0，中间那步不退订就直接红。
/// </summary>
public sealed class ComponentPreviewTimeZoneReleaseTests
{
    [AvaloniaFact]
    public void DroppingAPreview_ReleasesTheServiceReferenceToTheWidget()
    {
        var service = new TimeZoneService();
        var widget = new ClockWidget();

        Assert.Equal(0, SubscriberCount(service));

        widget.SetTimeZoneService(service);
        Assert.Equal(1, SubscriberCount(service));

        ComponentPreviewRuntimeQuiescer.Detach(widget);

        Assert.Equal(0, SubscriberCount(service));
    }

    [AvaloniaFact]
    public void DroppingAPreview_LeavesTheWidgetUsableForANewService()
    {
        var first = new TimeZoneService();
        var second = new TimeZoneService();
        var widget = new ClockWidget();

        widget.SetTimeZoneService(first);
        ComponentPreviewRuntimeQuiescer.Detach(widget);
        widget.SetTimeZoneService(second);

        Assert.Equal(0, SubscriberCount(first));
        Assert.Equal(1, SubscriberCount(second));
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
