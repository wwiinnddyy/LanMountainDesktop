using System;
using Avalonia.Controls;
using Avalonia.Media;
using LanMontainDesktop.Theme;

namespace LanMontainDesktop.Services;

public static class ThemeColorSystemService
{
    public static void ApplyThemeResources(
        IResourceDictionary resources,
        ThemeColorContext context)
    {
        var palette = BuildPalette(context);

        resources["AdaptiveTextPrimaryBrush"] = new SolidColorBrush(palette.TextPrimary);
        resources["AdaptiveTextSecondaryBrush"] = new SolidColorBrush(palette.TextSecondary);
        resources["AdaptiveTextMutedBrush"] = new SolidColorBrush(palette.TextMuted);
        resources["AdaptiveTextAccentBrush"] = new SolidColorBrush(palette.TextAccent);
        resources["AdaptiveNavTextBrush"] = new SolidColorBrush(palette.NavText);
        resources["AdaptiveNavSelectedTextBrush"] = new SolidColorBrush(palette.NavSelectedText);
        resources["AdaptiveNavItemBackgroundBrush"] = new SolidColorBrush(palette.NavItemBackground);
        resources["AdaptiveNavItemHoverBackgroundBrush"] = new SolidColorBrush(palette.NavItemHoverBackground);
        resources["AdaptiveNavItemSelectedBackgroundBrush"] = new SolidColorBrush(palette.NavItemSelectedBackground);
    }

    public static void ApplyThemeResources(
        IResourceDictionary resources,
        Color accentColor,
        bool isLightBackground,
        bool isLightNavBackground)
    {
        ApplyThemeResources(resources, new ThemeColorContext(
            accentColor,
            isLightBackground,
            isLightNavBackground,
            !isLightBackground));
    }

    private static AppThemePalette BuildPalette(ThemeColorContext context)
    {
        var textPrimary = context.IsLightBackground ? Color.Parse("#FF0B1220") : Color.Parse("#FFF8FAFC");
        var textSecondary = context.IsLightBackground ? Color.Parse("#FF1E293B") : Color.Parse("#FFE2E8F0");
        var textMuted = context.IsLightBackground ? Color.Parse("#FF475569") : Color.Parse("#FF94A3B8");
        var textAccent = context.IsLightBackground
            ? BlendColor(context.AccentColor, Color.Parse("#FF0B1220"), 0.20)
            : BlendColor(context.AccentColor, Color.Parse("#FFFFFFFF"), 0.16);

        var navText = context.IsLightNavBackground ? Color.Parse("#FF0B1220") : Color.Parse("#FFF8FAFC");
        var navSelectedText = Color.Parse("#FFFFFFFF");
        var navItemBackground = context.IsLightNavBackground ? Color.Parse("#40FFFFFF") : Color.Parse("#220F172A");
        var navItemHoverBackground = context.IsLightNavBackground ? Color.Parse("#66E2E8F0") : Color.Parse("#40334155");
        var navItemSelectedBackground = Color.FromArgb(
            0xCC,
            context.AccentColor.R,
            context.AccentColor.G,
            context.AccentColor.B);

        return new AppThemePalette(
            textPrimary,
            textSecondary,
            textMuted,
            textAccent,
            navText,
            navSelectedText,
            navItemBackground,
            navItemHoverBackground,
            navItemSelectedBackground);
    }

    private static Color BlendColor(Color from, Color to, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var inverse = 1 - ratio;
        var red = (byte)Math.Round((from.R * inverse) + (to.R * ratio));
        var green = (byte)Math.Round((from.G * inverse) + (to.G * ratio));
        var blue = (byte)Math.Round((from.B * inverse) + (to.B * ratio));
        return Color.FromRgb(red, green, blue);
    }
}
