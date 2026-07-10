using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LanMountainDesktop.DesktopEditing;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopEditOverlayPresenterTests
{
    [AvaloniaFact]
    public void CompositionOffsetHelperFallsBackWhenVisualIsUnavailable()
    {
        var service = new CompositionVisualAnimationService(_ => null);
        var target = new Border();

        var result = service.TrySetOffset(target, new Point(12, 34));

        Assert.False(result);
        Assert.False(service.TrySetOpacity(target, 0.5));
        Assert.False(service.TrySetUniformScale(target, 1.05));
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void CandidateRectUsesCanvasPlacement()
    {
        var presenter = new DesktopEditOverlayPresenter(new CompositionVisualAnimationService(_ => null));
        var root = Assert.IsType<Canvas>(presenter.Root);

        presenter.SetCandidateRect(new Rect(44, 58, 240, 160));

        var candidate = root.Children.OfType<Border>().Single(child => child is not DesktopEditGhostView);
        Assert.Equal(44, Canvas.GetLeft(candidate));
        Assert.Equal(58, Canvas.GetTop(candidate));
        Assert.Equal(240, candidate.Width);
        Assert.Equal(160, candidate.Height);
    }

    [AvaloniaFact]
    public void ShowPreservesPreviewAndCandidateCanvasPlacement()
    {
        var presenter = new DesktopEditOverlayPresenter(new CompositionVisualAnimationService(_ => null));
        var root = Assert.IsType<Canvas>(presenter.Root);

        presenter.SetPreviewRect(new Rect(16, 32, 180, 120));
        presenter.SetCandidateRect(new Rect(24, 40, 200, 140));
        presenter.Show();

        var ghost = root.Children.OfType<DesktopEditGhostView>().Single();
        var candidate = root.Children.OfType<Border>().Single(child => child is not DesktopEditGhostView);
        Assert.Equal(16, Canvas.GetLeft(ghost));
        Assert.Equal(32, Canvas.GetTop(ghost));
        Assert.Equal(24, Canvas.GetLeft(candidate));
        Assert.Equal(40, Canvas.GetTop(candidate));
    }
}
