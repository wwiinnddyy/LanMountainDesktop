using Avalonia;
using Avalonia.Media;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class WhiteboardWidgetLayoutSyncTests
{
    [Fact]
    public void ResolveViewportSize_PrefersViewportRootSize()
    {
        var resolution = WhiteboardWidget.ResolveViewportSize(
            viewportRootSize: new Size(320, 240),
            canvasBorderSize: new Size(200, 160),
            widgetSize: new Size(100, 80),
            currentCellSize: 48,
            baseWidthCells: 2);

        Assert.Equal(new Size(320, 240), resolution.Size);
        Assert.Equal("ViewportRoot", resolution.Source);
        Assert.False(resolution.IsFallback);
    }

    [Fact]
    public void ResolveViewportSize_FallsBackToCanvasBorderBeforeCellSize()
    {
        var resolution = WhiteboardWidget.ResolveViewportSize(
            viewportRootSize: new Size(0, 0),
            canvasBorderSize: new Size(260, 180),
            widgetSize: new Size(100, 80),
            currentCellSize: 48,
            baseWidthCells: 2);

        Assert.Equal(new Size(260, 180), resolution.Size);
        Assert.Equal("CanvasBorder", resolution.Source);
        Assert.False(resolution.IsFallback);
    }

    [Fact]
    public void ResolveViewportSize_UsesCellSizeFallbackOnlyWhenLayoutIsUnavailable()
    {
        var resolution = WhiteboardWidget.ResolveViewportSize(
            viewportRootSize: new Size(0, 0),
            canvasBorderSize: new Size(1, 1),
            widgetSize: new Size(0, 0),
            currentCellSize: 48,
            baseWidthCells: 2);

        Assert.Equal(new Size(96, 96), resolution.Size);
        Assert.Equal("Fallback", resolution.Source);
        Assert.True(resolution.IsFallback);
    }

    [Fact]
    public void ToOpaqueInkColor_ForcesColorPickerAlphaToVisibleInk()
    {
        var color = Color.FromArgb(0, 20, 40, 60);

        var inkColor = WhiteboardWidget.ToOpaqueInkColor(color);

        Assert.Equal((byte)255, inkColor.Alpha);
        Assert.Equal((byte)20, inkColor.Red);
        Assert.Equal((byte)40, inkColor.Green);
        Assert.Equal((byte)60, inkColor.Blue);
    }

    [Fact]
    public void WhiteboardWidget_DefinesDeferredViewportLayoutSynchronization()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "Views", "Components", "WhiteboardWidget.axaml.cs");
        var synchronizeSource = ExtractMethodSource(source, "SynchronizeViewportLayout");

        Assert.Contains("ViewportRoot.SizeChanged += OnViewportRootSizeChanged", source);
        Assert.Contains("QueueViewportLayoutSync(\"attached-loaded\")", source);
        Assert.Contains("DispatcherPriority.Loaded", source);
        Assert.Contains("ResolveViewportSize(", source);
        Assert.DoesNotContain("QueueNoteSave(", synchronizeSource);
    }

    [Fact]
    public void WhiteboardWidget_RestoresInkInputAfterColorPopupCloses()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "Views", "Components", "WhiteboardWidget.axaml.cs");
        var restoreSource = ExtractMethodSource(source, "RestoreInkInputAfterToolPopup");

        Assert.Contains("ColorPickerPopup.Closed += OnColorPickerPopupClosed", source);
        Assert.Contains("ColorPickerPopup.Closed -= OnColorPickerPopupClosed", source);
        Assert.Contains("ToOpaqueInkColor(e.NewColor)", source);
        Assert.Contains("SetToolMode(WhiteboardToolMode.Pen)", restoreSource);
        Assert.Contains("SynchronizeViewportLayout(reason)", restoreSource);
        Assert.Contains("InkCanvas.Focus", restoreSource);
    }

    [Fact]
    public void WhiteboardWidget_ColorPickerDoesNotPersistTransparentInk()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "Views", "Components", "WhiteboardWidget.axaml.cs");
        var colorChangedSource = ExtractMethodSource(source, "OnColorPickerColorChanged");

        Assert.DoesNotContain("color.A", colorChangedSource);
        Assert.DoesNotContain("e.NewColor.A", colorChangedSource);
        Assert.Contains("byte.MaxValue", source);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            if (File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                break;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }

    private static string ExtractMethodSource(string source, string methodName)
    {
        var methodIndex = source.IndexOf($"private void {methodName}(", StringComparison.Ordinal);
        Assert.True(methodIndex >= 0, $"Could not locate method '{methodName}'.");

        var braceIndex = source.IndexOf('{', methodIndex);
        Assert.True(braceIndex >= 0, $"Could not locate method body for '{methodName}'.");

        var depth = 0;
        for (var i = braceIndex; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(methodIndex, i - methodIndex + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method '{methodName}'.");
    }
}
