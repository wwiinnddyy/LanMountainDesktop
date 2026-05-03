using System;
using System.Collections.Generic;
using System.Linq;

namespace LanMountainDesktop.Services;

public static class ThemeAppearanceValues
{
    public const string ColorModeDefaultNeutral = "default_neutral";
    public const string ColorModeSeedMonet = "seed_monet";
    public const string ColorModeWallpaperMonet = "wallpaper_monet";

    public const string ColorSchemeFollowSystem = "follow_system";
    public const string ColorSchemeNative = "native";

    public const string ThemeModeLight = "light";
    public const string ThemeModeDark = "dark";
    public const string ThemeModeFollowSystem = "follow_system";

    public const string MaterialNone = "none";
    public const string MaterialAuto = "auto";
    public const string MaterialMica = "mica";
    public const string MaterialAcrylic = "acrylic";

    public static readonly IReadOnlyList<string> AllColorModes =
    [
        ColorModeDefaultNeutral,
        ColorModeSeedMonet,
        ColorModeWallpaperMonet
    ];

    public static readonly IReadOnlyList<string> AllMaterialModes =
    [
        MaterialAuto,
        MaterialNone,
        MaterialMica,
        MaterialAcrylic
    ];

    public static string NormalizeThemeColorMode(string? value, string? themeColor = null)
    {
        if (string.Equals(value, ColorModeDefaultNeutral, StringComparison.OrdinalIgnoreCase))
        {
            return ColorModeDefaultNeutral;
        }

        if (string.Equals(value, ColorModeWallpaperMonet, StringComparison.OrdinalIgnoreCase))
        {
            return ColorModeWallpaperMonet;
        }

        if (string.Equals(value, ColorModeSeedMonet, StringComparison.OrdinalIgnoreCase))
        {
            return ColorModeSeedMonet;
        }

        return string.IsNullOrWhiteSpace(themeColor)
            ? ColorModeDefaultNeutral
            : ColorModeSeedMonet;
    }

    public static string NormalizeSystemMaterialMode(string? value)
    {
        if (string.Equals(value, MaterialAuto, StringComparison.OrdinalIgnoreCase))
        {
            return MaterialAuto;
        }

        if (string.Equals(value, MaterialMica, StringComparison.OrdinalIgnoreCase))
        {
            return MaterialMica;
        }

        if (string.Equals(value, MaterialAcrylic, StringComparison.OrdinalIgnoreCase))
        {
            return MaterialAcrylic;
        }

        return MaterialNone;
    }

    public static string ResolveEffectiveSystemMaterialMode(string? value)
    {
        var normalized = NormalizeSystemMaterialMode(value);
        if (!string.Equals(normalized, MaterialAuto, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return MaterialMica;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0))
        {
            return MaterialAcrylic;
        }

        return MaterialNone;
    }

    public static IReadOnlyList<string> NormalizeAvailableMaterialModes(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return [MaterialAuto, MaterialNone];
        }

        var normalized = values
            .Select(NormalizeSystemMaterialMode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!normalized.Contains(MaterialAuto, StringComparer.OrdinalIgnoreCase))
        {
            normalized.Insert(0, MaterialAuto);
        }

        if (!normalized.Contains(MaterialNone, StringComparer.OrdinalIgnoreCase))
        {
            normalized.Insert(normalized.Count > 0 ? 1 : 0, MaterialNone);
        }

        return normalized;
    }
}
