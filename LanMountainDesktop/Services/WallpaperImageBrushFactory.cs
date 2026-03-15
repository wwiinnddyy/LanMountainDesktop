using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace LanMountainDesktop.Services;

internal static class WallpaperImageBrushFactory
{
    internal const string Fill = "Fill";
    internal const string Fit = "Fit";
    internal const string StretchMode = "Stretch";
    internal const string Center = "Center";
    internal const string Tile = "Tile";

    public static string NormalizePlacement(string? placement)
    {
        return placement switch
        {
            _ when string.Equals(placement, Fit, StringComparison.OrdinalIgnoreCase) => Fit,
            _ when string.Equals(placement, StretchMode, StringComparison.OrdinalIgnoreCase) => StretchMode,
            _ when string.Equals(placement, Center, StringComparison.OrdinalIgnoreCase) => Center,
            _ when string.Equals(placement, Tile, StringComparison.OrdinalIgnoreCase) => Tile,
            _ => Fill
        };
    }

    public static ImageBrush Create(Bitmap bitmap, string? placement)
    {
        var normalizedPlacement = NormalizePlacement(placement);
        var brush = new ImageBrush(bitmap)
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            Stretch = Stretch.UniformToFill,
            TileMode = TileMode.None
        };

        switch (normalizedPlacement)
        {
            case Fit:
                brush.Stretch = Stretch.Uniform;
                break;

            case StretchMode:
                brush.Stretch = Stretch.Fill;
                break;

            case Center:
                brush.Stretch = Stretch.None;
                break;

            case Tile:
                brush.AlignmentX = AlignmentX.Left;
                brush.AlignmentY = AlignmentY.Top;
                brush.Stretch = Stretch.None;
                brush.TileMode = TileMode.Tile;
                brush.DestinationRect = new RelativeRect(
                    0,
                    0,
                    Math.Max(1, bitmap.Size.Width),
                    Math.Max(1, bitmap.Size.Height),
                    RelativeUnit.Absolute);
                break;
        }

        return brush;
    }
}
