using System.Collections.Generic;
using Avalonia.Media;

namespace LanMountainDesktop.Models;

public sealed record MonetPalette(
    IReadOnlyList<Color> RecommendedColors,
    IReadOnlyList<Color> MonetColors);
