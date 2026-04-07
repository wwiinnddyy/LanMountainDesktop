namespace LanMountainDesktop.Settings.Core;

public static class GlobalAppearanceSettings
{
    public const string CornerRadiusStyleSharp = "Sharp";
    public const string CornerRadiusStyleBalanced = "Balanced";
    public const string CornerRadiusStyleRounded = "Rounded";
    public const string CornerRadiusStyleOpen = "Open";
    public const string DefaultCornerRadiusStyle = CornerRadiusStyleBalanced;

    /// <summary>
    /// Kept for backward compatibility during settings migration.
    /// New code should not reference this constant.
    /// </summary>
    public const double DefaultCornerRadiusScale = 1.0;
    public const double MinimumCornerRadiusScale = 0.0;

    public static string NormalizeCornerRadiusStyle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultCornerRadiusStyle;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, CornerRadiusStyleSharp, StringComparison.OrdinalIgnoreCase))
        {
            return CornerRadiusStyleSharp;
        }

        if (string.Equals(trimmed, CornerRadiusStyleBalanced, StringComparison.OrdinalIgnoreCase))
        {
            return CornerRadiusStyleBalanced;
        }

        if (string.Equals(trimmed, CornerRadiusStyleRounded, StringComparison.OrdinalIgnoreCase))
        {
            return CornerRadiusStyleRounded;
        }

        if (string.Equals(trimmed, CornerRadiusStyleOpen, StringComparison.OrdinalIgnoreCase))
        {
            return CornerRadiusStyleOpen;
        }

        return DefaultCornerRadiusStyle;
    }

    public static readonly IReadOnlyList<string> AllCornerRadiusStyles =
    [
        CornerRadiusStyleSharp,
        CornerRadiusStyleBalanced,
        CornerRadiusStyleRounded,
        CornerRadiusStyleOpen
    ];

    /// <summary>
    /// Backward compatibility: map previous scale values to the closest style.
    /// </summary>
    public static string MigrateScaleToStyle(double scale)
    {
        return scale switch
        {
            <= 0.60 => CornerRadiusStyleSharp,
            <= 1.20 => CornerRadiusStyleBalanced,
            <= 1.70 => CornerRadiusStyleRounded,
            _ => CornerRadiusStyleOpen
        };
    }
}
