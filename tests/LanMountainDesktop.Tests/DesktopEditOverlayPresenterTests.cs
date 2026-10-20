using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using LanMountainDesktop.DesktopEditing;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopEditOverlayPresenterTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UpdatingVisibleOverlayPreservesLayoutOffsetsUntilNextRender(bool movePreview, bool moveCandidate)
    {
        var presenter = new DesktopEditOverlayPresenter();
        var root = Assert.IsType<Canvas>(presenter.Root);
        var previewRect = new Rect(120, 180, 180, 120);
        var candidateRect = new Rect(128, 192, 200, 140);
        presenter.SetViewportSize(new Size(640, 480));
        presenter.SetPreviewRect(previewRect);
        presenter.SetCandidateRect(candidateRect);
        presenter.Show();

        var window = new Window { Width = 640, Height = 480, Content = root };
        window.Show();
        try
        {
            RenderFrame();
            var ghost = root.Children.OfType<DesktopEditGhostView>().Single();
            var candidate = root.Children.OfType<Border>().Single(child => child is not DesktopEditGhostView);
            var ghostVisual = Assert.IsAssignableFrom<CompositionVisual>(ElementComposition.GetElementVisual(ghost));
            var candidateVisual = Assert.IsAssignableFrom<CompositionVisual>(ElementComposition.GetElementVisual(candidate));
            Assert.Equal(previewRect.Position, new Point(ghostVisual.Offset.X, ghostVisual.Offset.Y));
            Assert.Equal(candidateRect.Position, new Point(candidateVisual.Offset.X, candidateVisual.Offset.Y));

            var nextPreviewRect = movePreview ? new Rect(232, 248, 180, 120) : previewRect;
            var nextCandidateRect = moveCandidate ? new Rect(240, 256, 200, 140) : candidateRect;
            presenter.SetPreviewRect(nextPreviewRect);
            presenter.SetCandidateRect(nextCandidateRect);

            // The compositor must keep the last rendered position until Canvas has arranged the next frame.
            Assert.Equal(previewRect.Position, new Point(ghostVisual.Offset.X, ghostVisual.Offset.Y));
            Assert.Equal(candidateRect.Position, new Point(candidateVisual.Offset.X, candidateVisual.Offset.Y));

            RenderFrame();
            Assert.Equal(nextPreviewRect.Position, new Point(ghostVisual.Offset.X, ghostVisual.Offset.Y));
            Assert.Equal(nextCandidateRect.Position, new Point(candidateVisual.Offset.X, candidateVisual.Offset.Y));
        }
        finally
        {
            window.Close();
        }
    }

    private static void RenderFrame()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

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
