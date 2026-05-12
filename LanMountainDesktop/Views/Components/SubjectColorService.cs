using System;
using Avalonia.Media;

namespace LanMountainDesktop.Views.Components;

internal static class SubjectColorService
{
    private static readonly (string Keyword, string Hex)[] Palette =
    [
        ("语文", "#5B8FF9"),
        ("数学", "#F6903D"),
        ("英语", "#5AD8A6"),
        ("物理", "#E8684A"),
        ("化学", "#9270CA"),
        ("生物", "#FF9845"),
        ("历史", "#1E9493"),
        ("地理", "#FF99C3"),
        ("政治", "#7262FD"),
        ("体育", "#78D3F8"),
        ("音乐", "#F25E7E"),
        ("美术", "#C2A1FD"),
    ];

    private const string DefaultHex = "#8B95A5";

    public static Color ResolveColor(string subjectName)
    {
        if (!string.IsNullOrWhiteSpace(subjectName))
        {
            foreach (var (keyword, hex) in Palette)
            {
                if (subjectName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return Color.Parse(hex);
                }
            }

            var hash = StableHash(subjectName);
            var index = (int)(hash % (uint)Palette.Length);
            return Color.Parse(Palette[index].Hex);
        }

        return Color.Parse(DefaultHex);
    }

    public static Color ResolveBackgroundColor(string subjectName, bool isCurrent)
    {
        var baseColor = ResolveColor(subjectName);
        var alpha = isCurrent ? 0.18 : 0.08;
        return new Color(
            (byte)(alpha * 255),
            baseColor.R,
            baseColor.G,
            baseColor.B);
    }

    public static Color ResolveForegroundColor(string subjectName, bool isNight)
    {
        var baseColor = ResolveColor(subjectName);
        if (isNight)
        {
            return new Color(
                0xFF,
                (byte)Math.Min(255, baseColor.R + 60),
                (byte)Math.Min(255, baseColor.G + 60),
                (byte)Math.Min(255, baseColor.B + 60));
        }

        return baseColor;
    }

    public static IBrush ResolveColorBrush(string subjectName)
    {
        return new SolidColorBrush(ResolveColor(subjectName));
    }

    public static IBrush ResolveBackgroundBrush(string subjectName, bool isCurrent)
    {
        return new SolidColorBrush(ResolveBackgroundColor(subjectName, isCurrent));
    }

    public static IBrush ResolveForegroundBrush(string subjectName, bool isNight)
    {
        return new SolidColorBrush(ResolveForegroundColor(subjectName, isNight));
    }

    private static uint StableHash(string input)
    {
        uint hash = 5381;
        foreach (var c in input)
        {
            hash = ((hash << 5) + hash) ^ (uint)c;
        }

        return hash;
    }
}
