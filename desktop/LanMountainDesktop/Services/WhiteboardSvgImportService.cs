using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Media;
using LanMountainDesktop.Models;
using SkiaSharp;

namespace LanMountainDesktop.Services;

public sealed class WhiteboardSvgImportResult
{
    public List<WhiteboardStrokeSnapshot> Strokes { get; init; } = [];

    public int SkippedPathCount { get; init; }
}

public static class WhiteboardSvgImportService
{
    public static WhiteboardSvgImportResult Import(Stream stream, double targetWidth, double targetHeight)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var document = XDocument.Load(stream);
        var root = document.Root;
        if (root is null)
        {
            return new WhiteboardSvgImportResult();
        }

        var viewport = ResolveViewport(root);
        var transform = ResolveTransform(viewport, targetWidth, targetHeight);
        var importedStrokes = new List<WhiteboardStrokeSnapshot>();
        var skippedPathCount = 0;

        foreach (var pathElement in root.Descendants().Where(static element =>
                     string.Equals(element.Name.LocalName, "path", StringComparison.OrdinalIgnoreCase)))
        {
            var pathData = pathElement.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(pathData))
            {
                skippedPathCount++;
                continue;
            }

            using var parsedPath = SKPath.ParseSvgPathData(pathData);
            if (parsedPath is null || parsedPath.IsEmpty)
            {
                skippedPathCount++;
                continue;
            }

            using var transformedPath = new SKPath(parsedPath);
            transformedPath.Transform(transform);

            var style = ParseStyle(pathElement.Attribute("style")?.Value);
            var fillValue = ResolvePresentationValue(pathElement, style, "fill");
            var strokeValue = ResolvePresentationValue(pathElement, style, "stroke");
            var strokeWidth = ResolveStrokeWidth(pathElement, style) * ResolveStrokeScale(transform);

            if (IsNone(fillValue) && TryParseSvgColor(strokeValue, out var strokeColor))
            {
                using var filledStrokePath = StrokePathToFillPath(transformedPath, strokeWidth);
                if (filledStrokePath.IsEmpty)
                {
                    skippedPathCount++;
                    continue;
                }

                importedStrokes.Add(CreateSnapshot(filledStrokePath, strokeColor, strokeWidth));
                continue;
            }

            if (!TryParseSvgColor(fillValue, out var fillColor) &&
                !TryParseSvgColor(strokeValue, out fillColor))
            {
                fillColor = SKColors.Black;
            }

            importedStrokes.Add(CreateSnapshot(transformedPath, fillColor, Math.Max(1d, strokeWidth)));
        }

        return new WhiteboardSvgImportResult
        {
            Strokes = importedStrokes,
            SkippedPathCount = skippedPathCount
        };
    }

    private static WhiteboardStrokeSnapshot CreateSnapshot(SKPath path, SKColor color, double inkThickness)
    {
        return new WhiteboardStrokeSnapshot
        {
            Color = ToHexColor(color),
            InkThickness = Math.Max(0.5d, inkThickness),
            IgnorePressure = true,
            PathSvgData = path.ToSvgPathData()
        };
    }

    private static SKPath StrokePathToFillPath(SKPath sourcePath, double strokeWidth)
    {
        var fillPath = new SKPath();
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(0.5f, (float)strokeWidth),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        if (!paint.GetFillPath(sourcePath, fillPath))
        {
            fillPath.Reset();
        }

        return fillPath;
    }

    private static SvgViewport ResolveViewport(XElement root)
    {
        var viewBox = root.Attribute("viewBox")?.Value;
        if (!string.IsNullOrWhiteSpace(viewBox))
        {
            var parts = viewBox
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseSvgLength)
                .ToArray();
            if (parts.Length == 4 && parts[2] > 0 && parts[3] > 0)
            {
                return new SvgViewport(parts[0], parts[1], parts[2], parts[3]);
            }
        }

        var width = ParseSvgLength(root.Attribute("width")?.Value);
        var height = ParseSvgLength(root.Attribute("height")?.Value);
        return new SvgViewport(0d, 0d, Math.Max(1d, width), Math.Max(1d, height));
    }

    private static SKMatrix ResolveTransform(SvgViewport viewport, double targetWidth, double targetHeight)
    {
        var scaleX = targetWidth > 0 ? targetWidth / viewport.Width : 1d;
        var scaleY = targetHeight > 0 ? targetHeight / viewport.Height : 1d;
        return new SKMatrix
        {
            ScaleX = (float)scaleX,
            SkewX = 0f,
            TransX = (float)(-viewport.X * scaleX),
            SkewY = 0f,
            ScaleY = (float)scaleY,
            TransY = (float)(-viewport.Y * scaleY),
            Persp0 = 0f,
            Persp1 = 0f,
            Persp2 = 1f
        };
    }

    private static double ResolveStrokeScale(SKMatrix transform)
    {
        return Math.Max(0.01d, (Math.Abs(transform.ScaleX) + Math.Abs(transform.ScaleY)) * 0.5d);
    }

    private static Dictionary<string, string> ParseStyle(string? value)
    {
        var style = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return style;
        }

        foreach (var declaration in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = declaration.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex >= declaration.Length - 1)
            {
                continue;
            }

            style[declaration[..separatorIndex].Trim()] = declaration[(separatorIndex + 1)..].Trim();
        }

        return style;
    }

    private static string? ResolvePresentationValue(
        XElement pathElement,
        IReadOnlyDictionary<string, string> style,
        string key)
    {
        if (pathElement.Attribute(key)?.Value is { } attributeValue)
        {
            return attributeValue;
        }

        return style.TryGetValue(key, out var styleValue) ? styleValue : null;
    }

    private static double ResolveStrokeWidth(XElement pathElement, IReadOnlyDictionary<string, string> style)
    {
        var value = ResolvePresentationValue(pathElement, style, "stroke-width");
        var parsed = ParseSvgLength(value);
        return parsed > 0 ? parsed : 1d;
    }

    private static double ParseSvgLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0d;
        }

        var trimmed = value.Trim();
        var end = 0;
        while (end < trimmed.Length &&
               (char.IsDigit(trimmed[end]) ||
                trimmed[end] is '.' or '-' or '+' or 'e' or 'E'))
        {
            end++;
        }

        return double.TryParse(
            trimmed[..end],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0d;
    }

    private static bool IsNone(string? value)
    {
        return string.Equals(value?.Trim(), "none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSvgColor(string? value, out SKColor color)
    {
        color = SKColors.Black;
        if (string.IsNullOrWhiteSpace(value) || IsNone(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "transparent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryParseShortHexColor(trimmed, out color))
        {
            return true;
        }

        try
        {
            var avaloniaColor = Color.Parse(trimmed);
            color = new SKColor(avaloniaColor.R, avaloniaColor.G, avaloniaColor.B, avaloniaColor.A);
            return color.Alpha > 0;
        }
        catch
        {
            return TryParseNamedColor(trimmed, out color);
        }
    }

    private static bool TryParseShortHexColor(string value, out SKColor color)
    {
        color = SKColors.Black;
        if (!value.StartsWith('#') || value.Length is not (4 or 5))
        {
            return false;
        }

        static byte Expand(char ch)
        {
            var value = Convert.ToByte(ch.ToString(), 16);
            return (byte)((value << 4) | value);
        }

        try
        {
            var r = Expand(value[1]);
            var g = Expand(value[2]);
            var b = Expand(value[3]);
            var a = value.Length == 5 ? Expand(value[4]) : (byte)255;
            color = new SKColor(r, g, b, a);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseNamedColor(string value, out SKColor color)
    {
        color = value.Trim().ToLowerInvariant() switch
        {
            "black" => SKColors.Black,
            "white" => SKColors.White,
            "red" => SKColors.Red,
            "green" => SKColors.Green,
            "blue" => SKColors.Blue,
            "yellow" => SKColors.Yellow,
            "gray" or "grey" => SKColors.Gray,
            _ => default
        };

        return color != default;
    }

    private static string ToHexColor(SKColor color)
    {
        return $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }

    private readonly record struct SvgViewport(double X, double Y, double Width, double Height);
}
