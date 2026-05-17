using System.Linq;
using Avalonia;
using Avalonia.Controls;
using LanMountainDesktop.DesktopEditing;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopEditOverlayPresenterTests
{
    [Fact]
    public void CompositionOffsetHelperFallsBackWhenVisualIsUnavailable()
    {
        var service = new CompositionVisualAnimationService(_ => null);
        var target = new Border();

        var result = service.TrySetOffset(target, new Point(12, 34));

        Assert.False(result);
        Assert.False(service.TrySetOpacity(target, 0.5));
        Assert.False(service.TrySetUniformScale(target, 1.05));
    }

    [Fact]
    public void PreviewRectUsesCanvasPlacementWhenCompositionIsUnavailable()
    {
        var presenter = new DesktopEditOverlayPresenter(new CompositionVisualAnimationService(_ => null));
        var root = Assert.IsType<Canvas>(presenter.Root);

        presenter.SetPreviewRect(new Rect(12, 34, 180, 120));

        var ghost = root.Children.OfType<DesktopEditGhostView>().Single();
        Assert.Equal(12, Canvas.GetLeft(ghost));
        Assert.Equal(34, Canvas.GetTop(ghost));
        Assert.Equal(180, ghost.Width);
        Assert.Equal(120, ghost.Height);
    }
}
