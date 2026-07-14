using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using LanMountainDesktop.Views;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopWidgetWindowChromeTests
{
    [AvaloniaFact]
    public void DirectRootBorderOwnsTheOnlyRoundedContourAndHasNoOuterShadow()
    {
        var componentRoot = new Border
        {
            BoxShadow = BoxShadows.Parse("0 8 24 #66000000")
        };
        var component = new UserControl
        {
            Content = componentRoot
        };

        var window = new DesktopWidgetWindow(component, "placement", 18d);
        window.UpdateComponentLayout(200d, 120d);

        var host = Assert.IsType<Border>(window.FindControl<Border>("ComponentContainer"));
        var editBorder = Assert.IsType<Border>(window.FindControl<Border>("EditModeBorder"));

        Assert.Equal(new CornerRadius(18d), componentRoot.CornerRadius);
        Assert.True(componentRoot.ClipToBounds);
        Assert.Equal(default(BoxShadows), componentRoot.BoxShadow);
        Assert.Equal(default, host.CornerRadius);
        Assert.False(host.ClipToBounds);
        Assert.Equal(new CornerRadius(18d), editBorder.CornerRadius);
        Assert.Null(editBorder.Effect);

        componentRoot.BoxShadow = BoxShadows.Parse("0 10 30 #88000000");
        componentRoot.CornerRadius = new CornerRadius(2d);
        componentRoot.ClipToBounds = false;

        Assert.Equal(default(BoxShadows), componentRoot.BoxShadow);
        Assert.Equal(new CornerRadius(18d), componentRoot.CornerRadius);
        Assert.True(componentRoot.ClipToBounds);
    }

    [AvaloniaFact]
    public void NonBorderRootUsesHostRoundedClip()
    {
        var component = new Grid();
        var window = new DesktopWidgetWindow(component, "placement", 14d);
        window.UpdateComponentLayout(180d, 100d);

        var host = Assert.IsType<Border>(window.FindControl<Border>("ComponentContainer"));

        Assert.Equal(new CornerRadius(14d), host.CornerRadius);
        Assert.True(host.ClipToBounds);
    }

    [AvaloniaFact]
    public void TemplatedContentControlDoesNotTransferContourOwnershipToItsContent()
    {
        var contentBorder = new Border
        {
            BoxShadow = BoxShadows.Parse("0 4 12 #44000000")
        };
        var component = new ContentControl
        {
            Content = contentBorder
        };
        var window = new DesktopWidgetWindow(component, "placement", 16d);
        window.UpdateComponentLayout(180d, 100d);

        var host = Assert.IsType<Border>(window.FindControl<Border>("ComponentContainer"));

        Assert.Equal(new CornerRadius(16d), host.CornerRadius);
        Assert.True(host.ClipToBounds);
        Assert.NotEqual(default, contentBorder.BoxShadow);
    }

    [AvaloniaFact]
    public void ReplacingUserControlRootTransfersContourOwnershipToTheNewBorder()
    {
        var firstRoot = new Border();
        var component = new UserControl { Content = firstRoot };
        var window = new DesktopWidgetWindow(component, "placement", 20d);

        var replacementRoot = new Border
        {
            BoxShadow = BoxShadows.Parse("0 8 20 #66000000")
        };
        component.Content = replacementRoot;

        Assert.Equal(new CornerRadius(20d), replacementRoot.CornerRadius);
        Assert.True(replacementRoot.ClipToBounds);
        Assert.Equal(default(BoxShadows), replacementRoot.BoxShadow);
    }

    [AvaloniaFact]
    public void HiddenComponentRootDoesNotFallBackToAFullWindowInteractiveRegion()
    {
        var componentRoot = new Border { IsVisible = false };
        var component = new UserControl { Content = componentRoot };
        var window = new DesktopWidgetWindow(component, "placement", 18d);
        var method = typeof(DesktopWidgetWindow).GetMethod(
            "ResolveLiveInteractiveRegion",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var result = method.Invoke(window, new object[] { new Rect(0, 0, 200, 120) });

        Assert.Null(result);
    }

    [AvaloniaFact]
    public void HiddenNonBorderComponentDoesNotUseTheVisibleHostAsItsInteractiveRegion()
    {
        var component = new Grid();
        var window = new DesktopWidgetWindow(component, "placement", 18d);
        window.UpdateComponentLayout(200d, 120d);
        var root = Assert.IsType<Grid>(window.FindControl<Grid>("RootGrid"));
        root.Measure(new Size(200d, 120d));
        root.Arrange(new Rect(0, 0, 200d, 120d));
        var method = typeof(DesktopWidgetWindow).GetMethod(
            "ResolveLiveInteractiveRegion",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var visibleResult = method.Invoke(window, new object[] { new Rect(0, 0, 200, 120) });
        component.IsVisible = false;
        var hiddenResult = method.Invoke(window, new object[] { new Rect(0, 0, 200, 120) });

        Assert.NotNull(visibleResult);
        Assert.Null(hiddenResult);
    }

    [AvaloniaFact]
    public void ResizeAdornerMatchesComponentBoundsAndKeepsEveryHandleInside()
    {
        var component = new Grid();
        var window = new DesktopWidgetWindow(component, "placement", 12d);
        window.UpdateComponentLayout(200d, 120d);

        var host = Assert.IsType<Border>(window.FindControl<Border>("ComponentContainer"));
        var root = Assert.IsType<Grid>(window.FindControl<Grid>("RootGrid"));
        var adorner = Assert.Single(root.Children.OfType<DesktopWidgetResizeAdorner>());

        adorner.Show();
        host.Measure(new Size(200d, 120d));
        host.Arrange(new Rect(0d, 0d, 200d, 120d));
        adorner.Measure(new Size(200d, 120d));
        adorner.Arrange(new Rect(0d, 0d, 200d, 120d));

        Assert.Equal(host.Bounds.Size, adorner.Bounds.Size);
        Assert.Equal(new Size(200d, 120d), adorner.Bounds.Size);

        foreach (var handle in adorner.Children.OfType<DesktopWidgetResizeHandle>())
        {
            var left = Canvas.GetLeft(handle);
            var top = Canvas.GetTop(handle);

            Assert.InRange(left, 0d, adorner.Bounds.Width - handle.Width);
            Assert.InRange(top, 0d, adorner.Bounds.Height - handle.Height);
            Assert.True(left + handle.Width <= adorner.Bounds.Width);
            Assert.True(top + handle.Height <= adorner.Bounds.Height);
        }
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public void ResizeMathKeepsPhysicalRightEdgeUnderPointerAcrossDpi(double currentScaling)
    {
        var result = DesktopWidgetWindow.CalculateResizedBounds(
            ResizeHandlePosition.Right,
            new Point(20d, 0d),
            new Size(200d, 120d),
            new PixelPoint(-500, 100),
            currentScaling);

        Assert.Equal(220d, result.width * currentScaling, 6);
        Assert.Equal(120d, result.height * currentScaling, 6);
        Assert.Equal(-500d, result.x);
        Assert.Equal(100d, result.y);
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public void ResizeMathKeepsOppositeEdgeFixedForLeftResizeAcrossDpi(double currentScaling)
    {
        var result = DesktopWidgetWindow.CalculateResizedBounds(
            ResizeHandlePosition.Left,
            new Point(20d, 0d),
            new Size(200d, 120d),
            new PixelPoint(-500, 100),
            currentScaling);

        var physicalWidth = result.width * currentScaling;
        Assert.Equal(180d, physicalWidth, 6);
        Assert.Equal(-480d, result.x);
        Assert.Equal(-300d, result.x + physicalWidth, 6);
        Assert.Equal(120d, result.height * currentScaling, 6);
    }
}
