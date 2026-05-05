using System.IO;
using System.Text;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class WhiteboardSvgImportServiceTests
{
    [Fact]
    public void Import_WithFilledPath_CreatesStaticStrokeSnapshot()
    {
        using var stream = ToStream("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 50">
              <path d="M 10 10 L 90 10 L 90 40 Z" fill="#112233" />
            </svg>
            """);

        var result = WhiteboardSvgImportService.Import(stream, targetWidth: 200, targetHeight: 100);

        Assert.Single(result.Strokes);
        Assert.Equal("#FF112233", result.Strokes[0].Color);
        Assert.Empty(result.Strokes[0].Points);
        Assert.False(string.IsNullOrWhiteSpace(result.Strokes[0].PathSvgData));
    }

    [Fact]
    public void Import_WithStrokePath_ConvertsStrokeToFilledPath()
    {
        using var stream = ToStream("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <path d="M 10 10 L 90 90" fill="none" stroke="red" stroke-width="6" />
            </svg>
            """);

        var result = WhiteboardSvgImportService.Import(stream, targetWidth: 100, targetHeight: 100);

        Assert.Single(result.Strokes);
        Assert.Equal("#FFFF0000", result.Strokes[0].Color);
        Assert.True(result.Strokes[0].InkThickness >= 6d);
        Assert.Empty(result.Strokes[0].Points);
        Assert.False(string.IsNullOrWhiteSpace(result.Strokes[0].PathSvgData));
    }

    [Fact]
    public void Import_WithStylePresentationAttributes_ParsesStyleValues()
    {
        using var stream = ToStream("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <path d="M 10 10 L 20 20" style="fill:none;stroke:#00FF00;stroke-width:4px" />
            </svg>
            """);

        var result = WhiteboardSvgImportService.Import(stream, targetWidth: 100, targetHeight: 100);

        Assert.Single(result.Strokes);
        Assert.Equal("#FF00FF00", result.Strokes[0].Color);
        Assert.True(result.Strokes[0].InkThickness >= 4d);
    }

    private static MemoryStream ToStream(string svg)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(svg));
    }
}
