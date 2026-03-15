using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Services;

public static class ThemeColorSystemService
{
    private const double WcagNormalTextContrast = 4.5;
    private const double WcagLargeTextContrast = 3.0;

    public static void ApplyThemeResources(
        IResourceDictionary resources,
        ThemeColorContext context)
    {
        var palette = BuildPalette(context);

        resources["AdaptivePrimaryBrush"] = new SolidColorBrush(palette.Primary);
        resources["AdaptiveSecondaryBrush"] = new SolidColorBrush(palette.Secondary);
        resources["AdaptiveAccentBrush"] = new SolidColorBrush(palette.Accent);
        resources["AdaptiveOnAccentBrush"] = new SolidColorBrush(palette.OnAccent);
        resources["AdaptiveSurfaceBaseBrush"] = new SolidColorBrush(palette.SurfaceBase);
        resources["AdaptiveSurfaceRaisedBrush"] = new SolidColorBrush(palette.SurfaceRaised);
        resources["AdaptiveSurfaceOverlayBrush"] = new SolidColorBrush(palette.SurfaceOverlay);
        resources["AdaptiveTextPrimaryBrush"] = new SolidColorBrush(palette.TextPrimary);
        resources["AdaptiveTextSecondaryBrush"] = new SolidColorBrush(palette.TextSecondary);
        resources["AdaptiveTextMutedBrush"] = new SolidColorBrush(palette.TextMuted);
        resources["AdaptiveTextAccentBrush"] = new SolidColorBrush(palette.TextAccent);
        resources["AdaptiveNavTextBrush"] = new SolidColorBrush(palette.NavText);
        resources["AdaptiveNavSelectedTextBrush"] = new SolidColorBrush(palette.NavSelectedText);
        resources["AdaptiveNavSelectionIndicatorBrush"] = new SolidColorBrush(palette.NavSelectionIndicator);
        resources["AdaptiveNavItemBackgroundBrush"] = new SolidColorBrush(palette.NavItemBackground);
        resources["AdaptiveNavItemHoverBackgroundBrush"] = new SolidColorBrush(palette.NavItemHoverBackground);
        resources["AdaptiveNavItemSelectedBackgroundBrush"] = new SolidColorBrush(palette.NavItemSelectedBackground);
        resources["AdaptiveToggleOnBrush"] = new SolidColorBrush(palette.ToggleOn);
        resources["AdaptiveToggleOffBrush"] = new SolidColorBrush(palette.ToggleOff);
        resources["AdaptiveToggleBorderBrush"] = new SolidColorBrush(palette.ToggleBorder);

        resources["SystemAccentColor"] = palette.Accent;
        resources["SystemAccentColorLight1"] = palette.AccentLight1;
        resources["SystemAccentColorLight2"] = palette.AccentLight2;
        resources["SystemAccentColorLight3"] = palette.AccentLight3;
        resources["SystemAccentColorDark1"] = palette.AccentDark1;
        resources["SystemAccentColorDark2"] = palette.AccentDark2;
        resources["SystemAccentColorDark3"] = palette.AccentDark3;
        resources["SystemAccentColorLight1Brush"] = new SolidColorBrush(palette.AccentLight1);
        resources["SystemAccentColorLight2Brush"] = new SolidColorBrush(palette.AccentLight2);
        resources["SystemAccentColorLight3Brush"] = new SolidColorBrush(palette.AccentLight3);
        resources["SystemAccentColorDark1Brush"] = new SolidColorBrush(palette.AccentDark1);
        resources["SystemAccentColorDark2Brush"] = new SolidColorBrush(palette.AccentDark2);
        resources["SystemAccentColorDark3Brush"] = new SolidColorBrush(palette.AccentDark3);
        resources["SystemAccentColorBrush"] = new SolidColorBrush(palette.Accent);
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
        var monetPalette = context.MonetPalette;
        var monetColors = context.MonetColors?.Where(color => color.A > 0).ToArray() ?? [];
        var accent = context.UseNeutralSurfaces
            ? context.AccentColor
            : monetPalette?.Primary ?? GetColorOrDefault(monetColors, 0, context.AccentColor);
        var secondarySeed = monetPalette?.Secondary
            ?? GetColorOrDefault(monetColors, 1, ColorMath.Blend(accent, Color.Parse("#FFFFFFFF"), 0.14));
        var tertiarySeed = monetPalette?.Tertiary
            ?? GetColorOrDefault(monetColors, 2, ColorMath.Blend(accent, Color.Parse("#FFFFFFFF"), 0.22));
        var neutralSeed = monetPalette?.Neutral
            ?? GetColorOrDefault(monetColors, 3, context.IsNightMode ? Color.Parse("#FF171C23") : Color.Parse("#FFF2F4F7"));
        var neutralVariantSeed = monetPalette?.NeutralVariant
            ?? GetColorOrDefault(monetColors, 4, context.IsNightMode ? Color.Parse("#FF20262E") : Color.Parse("#FFE8EDF2"));

        var accentLight1 = ColorMath.Blend(accent, Color.Parse("#FFFFFFFF"), 0.22);
        var accentLight2 = ColorMath.Blend(accent, Color.Parse("#FFFFFFFF"), 0.38);
        var accentLight3 = ColorMath.Blend(accent, Color.Parse("#FFFFFFFF"), 0.54);
        var accentDark1 = ColorMath.Blend(accent, Color.Parse("#FF0B1220"), 0.16);
        var accentDark2 = ColorMath.Blend(accent, Color.Parse("#FF0B1220"), 0.28);
        var accentDark3 = ColorMath.Blend(accent, Color.Parse("#FF020617"), 0.40);

        var primary = context.IsNightMode ? accentLight1 : accentDark1;
        var secondary = context.IsNightMode
            ? ColorMath.Blend(secondarySeed, Color.Parse("#FFFFFFFF"), 0.12)
            : ColorMath.Blend(secondarySeed, Color.Parse("#FF111827"), 0.10);

        var baseSurface = context.IsNightMode ? Color.Parse("#FF0B0F14") : Color.Parse("#FFF7F8FA");
        var raisedSurface = context.IsNightMode ? Color.Parse("#FF131922") : Color.Parse("#FFFFFFFF");
        var overlaySurface = context.IsNightMode ? Color.Parse("#FF171E28") : Color.Parse("#FFF1F4F8");
        var navSurfaceBase = context.IsLightNavBackground ? Color.Parse("#FFF8FAFC") : Color.Parse("#FF111827");

        var surfaceBase = context.UseNeutralSurfaces
            ? baseSurface
            : ColorMath.Blend(baseSurface, neutralSeed, context.IsNightMode ? 0.84 : 0.78);
        var surfaceRaised = context.UseNeutralSurfaces
            ? raisedSurface
            : ColorMath.Blend(raisedSurface, neutralVariantSeed, context.IsNightMode ? 0.72 : 0.60);
        var surfaceOverlayBase = context.UseNeutralSurfaces
            ? overlaySurface
            : ColorMath.Blend(overlaySurface, neutralVariantSeed, context.IsNightMode ? 0.76 : 0.64);
        var surfaceOverlay = Color.FromArgb(
            context.IsNightMode ? (byte)0xE8 : (byte)0xF2,
            surfaceOverlayBase.R,
            surfaceOverlayBase.G,
            surfaceOverlayBase.B);

        var textPrimaryPreferred = context.IsLightBackground ? Color.Parse("#FF0B1220") : Color.Parse("#FFF8FAFC");
        var textPrimary = ColorMath.EnsureContrast(textPrimaryPreferred, surfaceRaised, WcagNormalTextContrast);
        var textSecondary = ColorMath.EnsureContrast(
            ColorMath.Blend(textPrimary, surfaceRaised, context.IsNightMode ? 0.24 : 0.44),
            surfaceRaised,
            WcagLargeTextContrast);
        var textMuted = ColorMath.EnsureContrast(
            ColorMath.Blend(textPrimary, surfaceRaised, context.IsNightMode ? 0.40 : 0.58),
            surfaceRaised,
            WcagLargeTextContrast);
        var textAccent = context.IsLightBackground
            ? ColorMath.EnsureContrast(ColorMath.Blend(accent, Color.Parse("#FF0B1220"), 0.20), surfaceRaised, WcagNormalTextContrast)
            : ColorMath.EnsureContrast(ColorMath.Blend(accent, Color.Parse("#FFFFFFFF"), 0.16), surfaceRaised, WcagNormalTextContrast);

        var navSurface = context.UseNeutralSurfaces
            ? navSurfaceBase
            : ColorMath.Blend(navSurfaceBase, neutralSeed, context.IsNightMode ? 0.66 : 0.70);
        var navText = ColorMath.EnsureContrast(
            context.IsLightNavBackground ? Color.Parse("#FF0B1220") : Color.Parse("#FFF8FAFC"),
            navSurface,
            WcagNormalTextContrast);

        var selectedSurfaceForContrast = ColorMath.Blend(accent, navSurface, 0.18);
        var navSelectedText = ColorMath.EnsureContrast(Color.Parse("#FFFFFFFF"), selectedSurfaceForContrast, WcagNormalTextContrast);
        var navItemBackground = context.IsLightNavBackground
            ? Color.FromArgb(0x33, surfaceRaised.R, surfaceRaised.G, surfaceRaised.B)
            : Color.FromArgb(0x38, navSurface.R, navSurface.G, navSurface.B);
        var navItemHoverBackground = context.IsLightNavBackground
            ? ColorMath.WithAlpha(ColorMath.Blend(accentLight2, navSurface, 0.12), 0x64)
            : ColorMath.WithAlpha(ColorMath.Blend(accentDark1, navSurface, 0.18), 0x74);
        var navItemSelectedBackground = context.UseNeutralSurfaces
            ? ColorMath.WithAlpha(ColorMath.Blend(accent, navSurface, 0.24), context.IsNightMode ? (byte)0x8F : (byte)0x6A)
            : ColorMath.WithAlpha(ColorMath.Blend(accent, navSurface, context.IsNightMode ? 0.28 : 0.22), context.IsNightMode ? (byte)0x88 : (byte)0x72);
        var navSelectionIndicator = ColorMath.EnsureContrast(
            context.UseNeutralSurfaces ? accent : accentLight1,
            navSurface,
            WcagLargeTextContrast);

        var toggleOn = context.IsNightMode ? accent : accentDark1;
        var toggleOff = context.IsNightMode
            ? Color.FromArgb(0x88, neutralVariantSeed.R, neutralVariantSeed.G, neutralVariantSeed.B)
            : Color.FromArgb(0x88, neutralVariantSeed.R, neutralVariantSeed.G, neutralVariantSeed.B);
        var toggleBorder = context.IsNightMode
            ? ColorMath.WithAlpha(ColorMath.Blend(neutralVariantSeed, Color.Parse("#FFF8FAFC"), 0.20), 0x8C)
            : ColorMath.WithAlpha(ColorMath.Blend(tertiarySeed, Color.Parse("#FF334155"), 0.18), 0x78);
        var onAccent = ColorMath.EnsureContrast(Color.Parse("#FFFFFFFF"), accent, WcagNormalTextContrast);

        return new AppThemePalette(
            primary,
            secondary,
            accent,
            onAccent,
            accentLight1,
            accentLight2,
            accentLight3,
            accentDark1,
            accentDark2,
            accentDark3,
            surfaceBase,
            surfaceRaised,
            surfaceOverlay,
            textPrimary,
            textSecondary,
            textMuted,
            textAccent,
            navText,
            navSelectedText,
            navSelectionIndicator,
            navItemBackground,
            navItemHoverBackground,
            navItemSelectedBackground,
            toggleOn,
            toggleOff,
            toggleBorder);
    }

    private static Color GetColorOrDefault(IReadOnlyList<Color> colors, int index, Color fallback)
    {
        return index >= 0 && index < colors.Count
            ? colors[index]
            : fallback;
    }
}
