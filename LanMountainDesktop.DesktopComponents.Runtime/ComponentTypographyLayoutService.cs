using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Text;

namespace LanMountainDesktop.DesktopComponents.Runtime;

public static class ComponentTypographyLayoutService
{
    public static Size MeasureTextSize(
        string? text,
        double fontSize,
        FontWeight weight,
        double maxWidth,
        double lineHeight,
        FontFamily? fontFamily = null)
    {
        var probe = new TextBlock
        {
            Text = NormalizeText(text),
            FontSize = Math.Max(1, fontSize),
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = Math.Max(1, lineHeight)
        };

        if (fontFamily is not null)
        {
            probe.FontFamily = fontFamily;
        }

        probe.Measure(new Size(Math.Max(1, maxWidth), double.PositiveInfinity));
        return probe.DesiredSize;
    }

    public static double FitFontSize(
        string? text,
        double maxWidth,
        double maxHeight,
        int maxLines,
        double minFontSize,
        double maxFontSize,
        FontWeight weight,
        double lineHeightFactor,
        FontFamily? fontFamily = null)
    {
        var content = NormalizeText(text);
        var min = Math.Max(6, minFontSize);
        var max = Math.Max(min, maxFontSize);
        var low = min;
        var high = max;
        var best = min;

        for (var i = 0; i < 18; i++)
        {
            var candidate = (low + high) / 2d;
            var lineHeight = candidate * lineHeightFactor;
            var size = MeasureTextSize(content, candidate, weight, Math.Max(1, maxWidth), lineHeight, fontFamily);
            var lineCount = ResolveLineCount(size.Height, lineHeight);
            var fits = size.Height <= maxHeight + 0.6d && lineCount <= Math.Max(1, maxLines);

            if (fits)
            {
                best = candidate;
                low = candidate;
            }
            else
            {
                high = candidate;
            }
        }

        return best;
    }

    public static ComponentAdaptiveTextLayout FitAdaptiveTextLayout(
        string? text,
        double maxWidth,
        double maxHeight,
        int minLines,
        int maxLines,
        double minFontSize,
        double maxFontSize,
        IEnumerable<FontWeight>? weightCandidates = null,
        double lineHeightFactor = 1.1d,
        FontFamily? fontFamily = null)
    {
        var content = NormalizeText(text);
        var safeMinLines = Math.Max(1, minLines);
        var safeMaxLines = Math.Max(safeMinLines, maxLines);
        var linesByHeight = ResolveMaxLinesByHeight(maxHeight, minFontSize, lineHeightFactor, safeMinLines, safeMaxLines);

        var candidates = weightCandidates?.ToArray();
        if (candidates is null || candidates.Length == 0)
        {
            candidates = new[] { FontWeight.Normal };
        }

        ComponentAdaptiveTextLayout? best = null;
        foreach (var weight in candidates)
        {
            for (var lineLimit = linesByHeight; lineLimit >= safeMinLines; lineLimit--)
            {
                var fontSize = FitFontSize(
                    content,
                    maxWidth,
                    maxHeight,
                    lineLimit,
                    minFontSize,
                    maxFontSize,
                    weight,
                    lineHeightFactor,
                    fontFamily);

                var lineHeight = fontSize * lineHeightFactor;
                var measuredSize = MeasureTextSize(content, fontSize, weight, Math.Max(1, maxWidth), lineHeight, fontFamily);
                var measuredLineCount = ResolveLineCount(measuredSize.Height, lineHeight);
                var overflowLines = Math.Max(0, measuredLineCount - lineLimit);
                var overflowHeight = Math.Max(0, measuredSize.Height - maxHeight);
                var overflowScore = overflowLines * 1000d + overflowHeight;
                var candidate = new ComponentAdaptiveTextLayout(
                    fontSize,
                    weight,
                    lineLimit,
                    lineHeight,
                    overflowScore,
                    overflowLines == 0 && overflowHeight <= 0.6d,
                    measuredSize);

                if (best is null || IsBetterAdaptiveTextCandidate(candidate, best.Value))
                {
                    best = candidate;
                }
            }
        }

        if (best is not null)
        {
            return best.Value;
        }

        var fallbackFontSize = Math.Max(6, minFontSize);
        return new ComponentAdaptiveTextLayout(
            fallbackFontSize,
            FontWeight.Normal,
            safeMinLines,
            fallbackFontSize * lineHeightFactor,
            double.MaxValue,
            false,
            MeasureTextSize(content, fallbackFontSize, FontWeight.Normal, Math.Max(1, maxWidth), fallbackFontSize * lineHeightFactor, fontFamily));
    }

    public static int ResolveMaxLinesByHeight(
        double maxHeight,
        double minFontSize,
        double lineHeightFactor,
        int minLines,
        int maxLines)
    {
        var safeMinLines = Math.Max(1, minLines);
        var safeMaxLines = Math.Max(safeMinLines, maxLines);
        var lineHeight = Math.Max(1, Math.Max(6, minFontSize) * lineHeightFactor);
        var maxHeightWithTolerance = Math.Max(1, maxHeight + 0.6d);
        var linesByHeight = (int)Math.Floor(maxHeightWithTolerance / lineHeight);
        return Math.Clamp(linesByHeight, safeMinLines, safeMaxLines);
    }

    public static int ResolveLineCount(double measuredHeight, double lineHeight)
    {
        return Math.Max(1, (int)Math.Ceiling(measuredHeight / Math.Max(1, lineHeight)));
    }

    public static int EstimateDisplayUnits(
        double availableWidth,
        double unitWidth,
        double gapWidth = 0,
        double reservedWidth = 0,
        int minUnits = 1,
        int maxUnits = int.MaxValue)
    {
        var safeMinUnits = Math.Max(1, minUnits);
        var safeMaxUnits = Math.Max(safeMinUnits, maxUnits);
        var usableWidth = Math.Max(0, availableWidth - reservedWidth);
        var safeGapWidth = Math.Max(0, gapWidth);
        var raw = safeGapWidth > 0
            ? (usableWidth + safeGapWidth) / Math.Max(1, unitWidth + safeGapWidth)
            : usableWidth / Math.Max(1, unitWidth);

        return Math.Clamp((int)Math.Floor(raw), safeMinUnits, safeMaxUnits);
    }

    public static int CountTextDisplayUnits(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var total = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                continue;
            }

            total += IsCjkRune(rune) ? 2 : 1;
        }

        return total;
    }

    public static ComponentBoxLayout ResolveBadgeBox(
        double availableWidth,
        double availableHeight,
        double preferredSizeScale = 0.42d,
        double minSize = 10,
        double maxSize = 24,
        double insetScale = 0.2d)
    {
        var edge = Math.Min(Math.Max(1, availableWidth), Math.Max(1, availableHeight));
        var size = Math.Clamp(edge * preferredSizeScale, minSize, maxSize);
        var inset = Math.Clamp(size * insetScale, 0, size * 0.35d);
        return new ComponentBoxLayout(size, size, new Thickness(0, inset, 0, 0), new Thickness(inset));
    }

    public static ComponentBoxLayout ResolveGlyphBox(
        double availableWidth,
        double availableHeight,
        double preferredSizeScale = 0.50d,
        double minSize = 12,
        double maxSize = 28,
        double insetScale = 0.18d)
    {
        var edge = Math.Min(Math.Max(1, availableWidth), Math.Max(1, availableHeight));
        var size = Math.Clamp(edge * preferredSizeScale, minSize, maxSize);
        var inset = Math.Clamp(size * insetScale, 0, size * 0.30d);
        return new ComponentBoxLayout(size, size, new Thickness(inset), new Thickness(inset));
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return " ";
        }

        return string.Join(" ", text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsCjkRune(Rune rune)
    {
        var value = rune.Value;
        return (value >= 0x4E00 && value <= 0x9FFF) || // CJK Unified Ideographs
               (value >= 0x3400 && value <= 0x4DBF) || // CJK Unified Ideographs Extension A
               (value >= 0x20000 && value <= 0x2A6DF) || // CJK Unified Ideographs Extension B
               (value >= 0x2A700 && value <= 0x2B73F) || // CJK Unified Ideographs Extension C
               (value >= 0x2B740 && value <= 0x2B81F) || // CJK Unified Ideographs Extension D
               (value >= 0x2B820 && value <= 0x2CEAF) || // CJK Unified Ideographs Extension E/F
               (value >= 0xF900 && value <= 0xFAFF) ||   // CJK Compatibility Ideographs
               (value >= 0x2F800 && value <= 0x2FA1F) || // CJK Compatibility Ideographs Supplement
               (value >= 0x3040 && value <= 0x309F) ||   // Hiragana
               (value >= 0x30A0 && value <= 0x30FF) ||   // Katakana
               (value >= 0xAC00 && value <= 0xD7AF);     // Hangul Syllables
    }

    private static bool IsBetterAdaptiveTextCandidate(ComponentAdaptiveTextLayout candidate, ComponentAdaptiveTextLayout best)
    {
        if (candidate.FitsCompletely && !best.FitsCompletely)
        {
            return true;
        }

        if (!candidate.FitsCompletely && best.FitsCompletely)
        {
            return false;
        }

        if (candidate.FitsCompletely && best.FitsCompletely)
        {
            if (candidate.FontSize > best.FontSize + 0.12d)
            {
                return true;
            }

            if (Math.Abs(candidate.FontSize - best.FontSize) <= 0.12d && candidate.MaxLines < best.MaxLines)
            {
                return true;
            }

            return false;
        }

        if (candidate.OverflowScore < best.OverflowScore - 0.2d)
        {
            return true;
        }

        if (Math.Abs(candidate.OverflowScore - best.OverflowScore) <= 0.2d &&
            candidate.FontSize > best.FontSize + 0.12d)
        {
            return true;
        }

        if (Math.Abs(candidate.OverflowScore - best.OverflowScore) <= 0.2d &&
            Math.Abs(candidate.FontSize - best.FontSize) <= 0.12d &&
            candidate.MaxLines > best.MaxLines)
        {
            return true;
        }

        return false;
    }
}
