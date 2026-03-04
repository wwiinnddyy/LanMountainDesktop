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
        var accent = context.AccentColor;
        var accentLight1 = ColorMath.Blend(accent, Color.Parse("#FFFFFFFF"), 0.22);
        var accentLight2 = ColorMath.Blend(accent, Color.Parse("#FFFFFFFF"), 0.38);
        var accentLight3 = ColorMath.Blend(accent, Color.Parse("#FFFFFFFF"), 0.54);
        var accentDark1 = ColorMath.Blend(accent, Color.Parse("#FF0B1220"), 0.16);
        var accentDark2 = ColorMath.Blend(accent, Color.Parse("#FF0B1220"), 0.28);
        var accentDark3 = ColorMath.Blend(accent, Color.Parse("#FF020617"), 0.40);

        var primary = context.IsNightMode ? accentLight1 : accentDark1;
        var secondary = context.IsNightMode ? accentLight2 : accentDark2;

        var surfaceBase = context.IsNightMode ? Color.Parse("#FF0B1220") : Color.Parse("#FFF3F7FB");
        var surfaceRaised = context.IsNightMode ? Color.Parse("#FF1E293B") : Color.Parse("#FFFFFFFF");
        var surfaceOverlay = context.IsNightMode ? Color.Parse("#CC0B1220") : Color.Parse("#CCE2E8F0");

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

        var navSurface = context.IsLightNavBackground ? surfaceRaised : Color.Parse("#FF111827");
        var navText = ColorMath.EnsureContrast(
            context.IsLightNavBackground ? Color.Parse("#FF0B1220") : Color.Parse("#FFF8FAFC"),
            navSurface,
            WcagNormalTextContrast);

        var selectedSurfaceForContrast = ColorMath.Blend(accent, navSurface, 0.18);
        var navSelectedText = ColorMath.EnsureContrast(Color.Parse("#FFFFFFFF"), selectedSurfaceForContrast, WcagNormalTextContrast);
        var navItemBackground = context.IsLightNavBackground ? Color.Parse("#33FFFFFF") : Color.Parse("#2A0F172A");
        var navItemHoverBackground = context.IsLightNavBackground
            ? ColorMath.WithAlpha(ColorMath.Blend(accentLight2, Color.Parse("#FFFFFFFF"), 0.48), 0x66)
            : ColorMath.WithAlpha(ColorMath.Blend(accentDark1, Color.Parse("#33111827"), 0.32), 0x78);
        var navItemSelectedBackground = ColorMath.WithAlpha(accent, context.IsNightMode ? (byte)0xCE : (byte)0xD9);
        var navSelectionIndicator = ColorMath.EnsureContrast(accentLight1, navSurface, WcagLargeTextContrast);

        var toggleOn = context.IsNightMode ? accent : accentDark1;
        var toggleOff = context.IsNightMode ? Color.Parse("#66475569") : Color.Parse("#66CBD5E1");
        var toggleBorder = context.IsNightMode ? Color.Parse("#80E2E8F0") : Color.Parse("#8094A3B8");
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
}
