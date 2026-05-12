using System;
using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public enum MaterialWeatherCondition
{
    Unknown,
    Clear,
    PartlyCloudy,
    Cloudy,
    Rain,
    Storm,
    Snow,
    Fog,
    Haze
}

public sealed record MaterialWeatherPalette(
    Color BackgroundTop,
    Color BackgroundBottom,
    Color PrimaryShape,
    Color SecondaryShape,
    Color AccentShape,
    Color TextPrimary,
    Color TextSecondary,
    Color SurfaceTint,
    Color OverlayTint,
    Color SurfaceColor,
    Color SurfaceVariantColor,
    Color OutlineColor);

public static class MaterialWeatherVisualTheme
{
    public static MaterialWeatherCondition ResolveCondition(WeatherSnapshot? snapshot)
    {
        return ResolveCondition(snapshot?.Current.WeatherCode, snapshot?.Current.WeatherText);
    }

    public static MaterialWeatherCondition ResolveCondition(int? weatherCode, string? weatherText)
    {
        if (weatherCode.HasValue)
        {
            return weatherCode.Value switch
            {
                0 => MaterialWeatherCondition.Clear,
                1 => MaterialWeatherCondition.PartlyCloudy,
                2 => MaterialWeatherCondition.Cloudy,
                3 or 7 or 8 or 9 or 10 or 11 or 12 or 19 or 21 or 22 or 23 or 24 or 25 or 301 => MaterialWeatherCondition.Rain,
                4 or 5 => MaterialWeatherCondition.Storm,
                6 or 13 or 14 or 15 or 16 or 17 or 26 or 27 or 28 or 302 => MaterialWeatherCondition.Snow,
                18 or 32 or 49 or 57 or 58 => MaterialWeatherCondition.Fog,
                20 or 29 or 30 or 31 or 53 or 54 or 55 or 56 => MaterialWeatherCondition.Haze,
                _ => MaterialWeatherCondition.Unknown
            };
        }

        var text = weatherText?.Trim().ToLowerInvariant() ?? string.Empty;
        if (text.Contains("rain") || text.Contains('\u96e8')) return MaterialWeatherCondition.Rain;
        if (text.Contains("storm") || text.Contains("thunder") || text.Contains('\u96f7')) return MaterialWeatherCondition.Storm;
        if (text.Contains("snow") || text.Contains('\u96ea')) return MaterialWeatherCondition.Snow;
        if (text.Contains("fog") || text.Contains("mist") || text.Contains('\u96fe')) return MaterialWeatherCondition.Fog;
        if (text.Contains("haze") || text.Contains("dust") || text.Contains('\u973e')) return MaterialWeatherCondition.Haze;
        if (text.Contains("cloud") || text.Contains('\u4e91') || text.Contains('\u9634')) return MaterialWeatherCondition.Cloudy;
        if (text.Contains("clear") || text.Contains("sun") || text.Contains('\u6674')) return MaterialWeatherCondition.Clear;
        return MaterialWeatherCondition.Unknown;
    }

    public static MaterialWeatherPalette ResolvePalette(string? styleId, MaterialWeatherCondition condition, bool isNight)
    {
        var normalized = WeatherVisualStyleCatalog.Normalize(styleId);
        return normalized switch
        {
            WeatherVisualStyleId.Geometric => ResolveGeometricPalette(condition, isNight),
            WeatherVisualStyleId.Breezy => ResolveBreezyPalette(condition, isNight),
            WeatherVisualStyleId.LemonFlutter => ResolveLemonPalette(condition, isNight),
            _ => ResolveGooglePalette(condition, isNight)
        };
    }

    public static MaterialWeatherPalette ResolvePalette(MaterialWeatherCondition condition, bool isNight)
    {
        return ResolveGooglePalette(condition, isNight);
    }

    private static MaterialWeatherPalette ResolveGooglePalette(MaterialWeatherCondition condition, bool isNight)
    {
        if (isNight)
        {
            return condition switch
            {
                MaterialWeatherCondition.Clear => P("#0D47A1", "#1A237E", "#FFD54F", "#6EA8FE", "#B9C7FF", "#E8EAF6", "#9FA8DA", "#1A237E", "#1A000000"),
                MaterialWeatherCondition.PartlyCloudy => P("#1565C0", "#283593", "#FFD54F", "#8EA2D9", "#B9C7FF", "#E8EAF6", "#9FA8DA", "#283593", "#1A000000"),
                MaterialWeatherCondition.Cloudy => P("#37474F", "#455A64", "#B0BEC5", "#78909C", "#CFD8DC", "#ECEFF1", "#90A4AE", "#455A64", "#1A000000"),
                MaterialWeatherCondition.Rain or MaterialWeatherCondition.Storm => P("#263238", "#37474F", "#78909C", "#546E7A", "#90A4AE", "#CFD8DC", "#90A4AE", "#37474F", "#1A000000"),
                MaterialWeatherCondition.Snow => P("#1A237E", "#283593", "#E8EAF6", "#9FA8DA", "#FFFFFF", "#F5F5F5", "#B0BEC5", "#283593", "#1A000000"),
                MaterialWeatherCondition.Fog or MaterialWeatherCondition.Haze => P("#455A64", "#546E7A", "#B0BEC5", "#78909C", "#CFD8DC", "#ECEFF1", "#90A4AE", "#546E7A", "#1A000000"),
                _ => P("#0D47A1", "#1A237E", "#FFD54F", "#6EA8FE", "#B9C7FF", "#E8EAF6", "#9FA8DA", "#1A237E", "#1A000000")
            };
        }

        return condition switch
        {
            MaterialWeatherCondition.Clear => P("#4FC3F7", "#B3E5FC", "#FFD54F", "#FFF176", "#4FC3F7", "#0D47A1", "#1565C0", "#B3E5FC", "#14FFFFFF"),
            MaterialWeatherCondition.PartlyCloudy => P("#81D4FA", "#E1F5FE", "#FFD54F", "#E1F5FE", "#81D4FA", "#0D47A1", "#1565C0", "#E1F5FE", "#12FFFFFF"),
            MaterialWeatherCondition.Cloudy => P("#90A4AE", "#CFD8DC", "#CFD8DC", "#B0BEC5", "#78909C", "#263238", "#455A64", "#CFD8DC", "#10FFFFFF"),
            MaterialWeatherCondition.Rain => P("#78909C", "#B0BEC5", "#90A4AE", "#78909C", "#546E7A", "#263238", "#37474F", "#B0BEC5", "#0EFFFFFF"),
            MaterialWeatherCondition.Storm => P("#546E7A", "#78909C", "#607D8B", "#546E7A", "#FFCE5C", "#1A1A2E", "#37474F", "#78909C", "#12FFFFFF"),
            MaterialWeatherCondition.Snow => P("#E1F5FE", "#FFFFFF", "#FFFFFF", "#B3E5FC", "#81D4FA", "#0D47A1", "#1565C0", "#FFFFFF", "#18FFFFFF"),
            MaterialWeatherCondition.Fog or MaterialWeatherCondition.Haze => P("#B0BEC5", "#ECEFF1", "#CFD8DC", "#B0BEC5", "#90A4AE", "#37474F", "#546E7A", "#ECEFF1", "#10FFFFFF"),
            _ => P("#4FC3F7", "#B3E5FC", "#FFD54F", "#FFF176", "#4FC3F7", "#0D47A1", "#1565C0", "#B3E5FC", "#14FFFFFF")
        };
    }

    private static MaterialWeatherPalette ResolveGeometricPalette(MaterialWeatherCondition condition, bool isNight)
    {
        if (isNight)
        {
            return condition switch
            {
                MaterialWeatherCondition.Clear => P("#0A0E27", "#1A1A3E", "#1A237E", "#283593", "#3F51B5", "#C5CAE9", "#7986CB", "#1A1A3E", "#0CFFFFFF"),
                MaterialWeatherCondition.PartlyCloudy => P("#0D1033", "#1E1E4A", "#1A237E", "#303F9F", "#5C6BC0", "#C5CAE9", "#7986CB", "#1E1E4A", "#0CFFFFFF"),
                MaterialWeatherCondition.Cloudy => P("#1A1A2E", "#2D2D44", "#37474F", "#455A64", "#607D8B", "#CFD8DC", "#90A4AE", "#2D2D44", "#0AFFFFFF"),
                MaterialWeatherCondition.Rain or MaterialWeatherCondition.Storm => P("#0A0E27", "#1A1A3E", "#1A237E", "#303F9F", "#3F51B5", "#C5CAE9", "#7986CB", "#1A1A3E", "#0EFFFFFF"),
                MaterialWeatherCondition.Snow => P("#1A237E", "#283593", "#E8EAF6", "#9FA8DA", "#C5CAE9", "#E8EAF6", "#9FA8DA", "#283593", "#0CFFFFFF"),
                MaterialWeatherCondition.Fog or MaterialWeatherCondition.Haze => P("#1A1A2E", "#37474F", "#455A64", "#546E7A", "#78909C", "#CFD8DC", "#90A4AE", "#37474F", "#0AFFFFFF"),
                _ => P("#0A0E27", "#1A1A3E", "#1A237E", "#283593", "#3F51B5", "#C5CAE9", "#7986CB", "#1A1A3E", "#0CFFFFFF")
            };
        }

        return condition switch
        {
            MaterialWeatherCondition.Clear => P("#1A237E", "#3949AB", "#5C6BC0", "#3F51B5", "#7986CB", "#E8EAF6", "#9FA8DA", "#3949AB", "#08FFFFFF"),
            MaterialWeatherCondition.PartlyCloudy => P("#283593", "#5C6BC0", "#7986CB", "#5C6BC0", "#9FA8DA", "#E8EAF6", "#9FA8DA", "#5C6BC0", "#08FFFFFF"),
            MaterialWeatherCondition.Cloudy => P("#37474F", "#607D8B", "#78909C", "#607D8B", "#90A4AE", "#ECEFF1", "#B0BEC5", "#607D8B", "#08FFFFFF"),
            MaterialWeatherCondition.Rain => P("#1A237E", "#3F51B5", "#5C6BC0", "#3F51B5", "#7986CB", "#E8EAF6", "#9FA8DA", "#3F51B5", "#0AFFFFFF"),
            MaterialWeatherCondition.Storm => P("#1A1A2E", "#3F51B5", "#5C6BC0", "#303F9F", "#FFCE5C", "#E8EAF6", "#9FA8DA", "#3F51B5", "#0CFFFFFF"),
            MaterialWeatherCondition.Snow => P("#E8EAF6", "#C5CAE9", "#FFFFFF", "#9FA8DA", "#7986CB", "#1A237E", "#283593", "#C5CAE9", "#0CFFFFFF"),
            MaterialWeatherCondition.Fog or MaterialWeatherCondition.Haze => P("#455A64", "#78909C", "#90A4AE", "#78909C", "#B0BEC5", "#ECEFF1", "#B0BEC5", "#78909C", "#08FFFFFF"),
            _ => P("#1A237E", "#3949AB", "#5C6BC0", "#3F51B5", "#7986CB", "#E8EAF6", "#9FA8DA", "#3949AB", "#08FFFFFF")
        };
    }

    private static MaterialWeatherPalette ResolveBreezyPalette(MaterialWeatherCondition condition, bool isNight)
    {
        if (isNight)
        {
            return condition switch
            {
                MaterialWeatherCondition.Clear => P("#006064", "#00838F", "#4DD0E1", "#00ACC1", "#80DEEA", "#E0F7FA", "#80DEEA", "#00838F", "#0E000000"),
                MaterialWeatherCondition.PartlyCloudy => P("#00695C", "#00897B", "#4DB6AC", "#009688", "#80CBC4", "#E0F2F1", "#80CBC4", "#00897B", "#0E000000"),
                MaterialWeatherCondition.Cloudy => P("#37474F", "#546E7A", "#78909C", "#607D8B", "#90A4AE", "#ECEFF1", "#B0BEC5", "#546E7A", "#0E000000"),
                MaterialWeatherCondition.Rain or MaterialWeatherCondition.Storm => P("#004D40", "#00695C", "#4DB6AC", "#00897B", "#80CBC4", "#E0F2F1", "#80CBC4", "#00695C", "#10000000"),
                MaterialWeatherCondition.Snow => P("#006064", "#00838F", "#E0F7FA", "#80DEEA", "#FFFFFF", "#E0F7FA", "#80DEEA", "#00838F", "#0E000000"),
                MaterialWeatherCondition.Fog or MaterialWeatherCondition.Haze => P("#37474F", "#546E7A", "#78909C", "#607D8B", "#B0BEC5", "#ECEFF1", "#B0BEC5", "#546E7A", "#0E000000"),
                _ => P("#006064", "#00838F", "#4DD0E1", "#00ACC1", "#80DEEA", "#E0F7FA", "#80DEEA", "#00838F", "#0E000000")
            };
        }

        return condition switch
        {
            MaterialWeatherCondition.Clear => P("#4DD0E1", "#80DEEA", "#26C6DA", "#00BCD4", "#B2EBF2", "#004D40", "#00695C", "#80DEEA", "#12FFFFFF"),
            MaterialWeatherCondition.PartlyCloudy => P("#4FC3F7", "#B2EBF2", "#29B6F6", "#03A9F4", "#E1F5FE", "#004D40", "#00695C", "#B2EBF2", "#12FFFFFF"),
            MaterialWeatherCondition.Cloudy => P("#80CBC4", "#B2DFDB", "#A7D9D2", "#80CBC4", "#B2DFDB", "#004D40", "#00695C", "#B2DFDB", "#10FFFFFF"),
            MaterialWeatherCondition.Rain => P("#4DB6AC", "#80CBC4", "#66BB6A", "#4DB6AC", "#A7D9D2", "#004D40", "#00695C", "#80CBC4", "#0EFFFFFF"),
            MaterialWeatherCondition.Storm => P("#26A69A", "#4DB6AC", "#00897B", "#26A69A", "#FFCE5C", "#004D40", "#00695C", "#4DB6AC", "#12FFFFFF"),
            MaterialWeatherCondition.Snow => P("#E0F7FA", "#FFFFFF", "#FFFFFF", "#B2EBF2", "#80DEEA", "#004D40", "#00695C", "#FFFFFF", "#16FFFFFF"),
            MaterialWeatherCondition.Fog or MaterialWeatherCondition.Haze => P("#80CBC4", "#E0F7FA", "#A7D9D2", "#80CBC4", "#B2DFDB", "#004D40", "#00695C", "#E0F7FA", "#10FFFFFF"),
            _ => P("#4DD0E1", "#80DEEA", "#26C6DA", "#00BCD4", "#B2EBF2", "#004D40", "#00695C", "#80DEEA", "#12FFFFFF")
        };
    }

    private static MaterialWeatherPalette ResolveLemonPalette(MaterialWeatherCondition condition, bool isNight)
    {
        if (isNight)
        {
            return condition switch
            {
                MaterialWeatherCondition.Clear => P("#1A237E", "#311B92", "#FFD54F", "#7C4DFF", "#B388FF", "#E8EAF6", "#B39DDB", "#311B92", "#12000000"),
                MaterialWeatherCondition.PartlyCloudy => P("#283593", "#4A148C", "#FFD54F", "#7C4DFF", "#B388FF", "#EDE7F6", "#B39DDB", "#4A148C", "#12000000"),
                MaterialWeatherCondition.Cloudy => P("#37474F", "#4E342E", "#8D6E63", "#6D4C41", "#BCAAA4", "#EFEBE9", "#BCAAA4", "#4E342E", "#12000000"),
                MaterialWeatherCondition.Rain or MaterialWeatherCondition.Storm => P("#1A1A2E", "#311B92", "#7C4DFF", "#5C6BC0", "#9FA8DA", "#D1C4E9", "#9575CD", "#311B92", "#14000000"),
                MaterialWeatherCondition.Snow => P("#1A237E", "#311B92", "#E8EAF6", "#B39DDB", "#FFFFFF", "#F3E5F5", "#CE93D8", "#311B92", "#12000000"),
                MaterialWeatherCondition.Fog or MaterialWeatherCondition.Haze => P("#4E342E", "#5D4037", "#8D6E63", "#6D4C41", "#BCAAA4", "#EFEBE9", "#BCAAA4", "#5D4037", "#12000000"),
                _ => P("#1A237E", "#311B92", "#FFD54F", "#7C4DFF", "#B388FF", "#E8EAF6", "#B39DDB", "#311B92", "#12000000")
            };
        }

        return condition switch
        {
            MaterialWeatherCondition.Clear => P("#FFB74D", "#FFF176", "#FF9800", "#FFC107", "#FFE082", "#4E342E", "#6D4C41", "#FFF176", "#0EFFFFFF"),
            MaterialWeatherCondition.PartlyCloudy => P("#FF8A65", "#FFCC80", "#FF7043", "#FFA726", "#FFE0B2", "#4E342E", "#6D4C41", "#FFCC80", "#0EFFFFFF"),
            MaterialWeatherCondition.Cloudy => P("#BCAAA4", "#D7CCC8", "#A1887F", "#8D6E63", "#BCAAA4", "#3E2723", "#5D4037", "#D7CCC8", "#0EFFFFFF"),
            MaterialWeatherCondition.Rain => P("#90A4AE", "#B0BEC5", "#78909C", "#607D8B", "#B0BEC5", "#263238", "#37474F", "#B0BEC5", "#0CFFFFFF"),
            MaterialWeatherCondition.Storm => P("#78909C", "#90A4AE", "#607D8B", "#546E7A", "#FFCE5C", "#1A1A2E", "#37474F", "#90A4AE", "#10FFFFFF"),
            MaterialWeatherCondition.Snow => P("#FFF9C4", "#FFFFFF", "#FFFFFF", "#FFF9C4", "#FFF176", "#4E342E", "#6D4C41", "#FFFFFF", "#12FFFFFF"),
            MaterialWeatherCondition.Fog or MaterialWeatherCondition.Haze => P("#D7CCC8", "#EFEBE9", "#BCAAA4", "#A1887F", "#D7CCC8", "#3E2723", "#5D4037", "#EFEBE9", "#0CFFFFFF"),
            _ => P("#FFB74D", "#FFF176", "#FF9800", "#FFC107", "#FFE082", "#4E342E", "#6D4C41", "#FFF176", "#0EFFFFFF")
        };
    }

    public static string ResolveDisplayText(WeatherSnapshot? snapshot, string fallback)
    {
        var text = snapshot?.Current.WeatherText?.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text!;
        }

        return ResolveCondition(snapshot) switch
        {
            MaterialWeatherCondition.Clear => "Clear",
            MaterialWeatherCondition.PartlyCloudy => "Partly cloudy",
            MaterialWeatherCondition.Cloudy => "Cloudy",
            MaterialWeatherCondition.Rain => "Rain",
            MaterialWeatherCondition.Storm => "Storm",
            MaterialWeatherCondition.Snow => "Snow",
            MaterialWeatherCondition.Fog => "Fog",
            MaterialWeatherCondition.Haze => "Haze",
            _ => fallback
        };
    }

    private static MaterialWeatherPalette P(string bgTop, string bgBottom, string primary, string secondary, string accent, string textPrimary, string textSecondary, string surfaceTint, string overlayTint)
    {
        var isDark = IsDarkBackground(C(bgTop), C(bgBottom));
        var surfaceColor = isDark ? Color.Parse("#1AFFFFFF") : Color.Parse("#14000000");
        var surfaceVariantColor = isDark ? Color.Parse("#12FFFFFF") : Color.Parse("#0E000000");
        var outlineColor = isDark ? Color.Parse("#24FFFFFF") : Color.Parse("#1C000000");
        return new MaterialWeatherPalette(
            C(bgTop), C(bgBottom), C(primary), C(secondary), C(accent),
            C(textPrimary), C(textSecondary), C(surfaceTint), C(overlayTint),
            surfaceColor, surfaceVariantColor, outlineColor);
    }

    private static bool IsDarkBackground(Color top, Color bottom)
    {
        var luminance = (0.299 * top.R + 0.587 * top.G + 0.114 * top.B) / 255;
        return luminance < 0.5;
    }

    private static Color C(string value)
    {
        return Color.Parse(value);
    }
}
