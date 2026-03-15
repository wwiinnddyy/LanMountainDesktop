using System.Collections.Generic;
using Avalonia.Media;

namespace LanMountainDesktop.Models;

public sealed record MonetPalette
{
    public MonetPalette(
        IReadOnlyList<Color> recommendedColors,
        Color seed,
        Color primary,
        Color secondary,
        Color tertiary,
        Color neutral,
        Color neutralVariant)
    {
        RecommendedColors = recommendedColors;
        Seed = seed;
        Primary = primary;
        Secondary = secondary;
        Tertiary = tertiary;
        Neutral = neutral;
        NeutralVariant = neutralVariant;
        MonetColors =
        [
            primary,
            secondary,
            tertiary,
            neutral,
            neutralVariant
        ];
    }

    public IReadOnlyList<Color> RecommendedColors { get; }

    public IReadOnlyList<Color> MonetColors { get; }

    public Color Seed { get; }

    public Color Primary { get; }

    public Color Secondary { get; }

    public Color Tertiary { get; }

    public Color Neutral { get; }

    public Color NeutralVariant { get; }
}
