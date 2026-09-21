using System.Collections.Generic;

using Avalonia.Media;

namespace LanMountainDesktop.Theme;

/// <summary>
/// 在半透明面板上挑一个读得清的前景色：取第一个在**所有**背景样本上都达到最低对比度的候选色;
/// 一个都不达标时退而取对比度最高的那个，而不是放弃对比度。
/// 此前这 34 行被 6 个学习组件各抄了一遍。
/// </summary>
public static class AdaptiveBrushFactory
{
    private static readonly Color FallbackColor = Color.Parse("#FFFFFFFF");

    public static SolidColorBrush Create(
        IReadOnlyList<Color> backgroundSamples,
        IReadOnlyList<Color> colorCandidates,
        double minContrast)
    {
        return new SolidColorBrush(Pick(backgroundSamples, colorCandidates, minContrast));
    }

    public static Color Pick(
        IReadOnlyList<Color> backgroundSamples,
        IReadOnlyList<Color> colorCandidates,
        double minContrast)
    {
        if (colorCandidates.Count == 0)
        {
            return FallbackColor;
        }

        for (var index = 0; index < colorCandidates.Count; index++)
        {
            var candidate = colorCandidates[index];
            if (ColorMath.MinContrastRatio(candidate, backgroundSamples) >= minContrast)
            {
                return candidate;
            }
        }

        var best = colorCandidates[0];
        var bestContrast = ColorMath.MinContrastRatio(best, backgroundSamples);
        for (var index = 1; index < colorCandidates.Count; index++)
        {
            var candidate = colorCandidates[index];
            var contrast = ColorMath.MinContrastRatio(candidate, backgroundSamples);
            if (contrast > bestContrast)
            {
                best = candidate;
                bestContrast = contrast;
            }
        }

        return best;
    }
}
