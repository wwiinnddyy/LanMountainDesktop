using System.Linq;
using Avalonia.Media;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Services;

internal sealed class MaterialSurfaceService : IMaterialSurfaceService
{
    public AppearanceMaterialSurface GetSurface(ThemeColorContext context, MaterialSurfaceRole role)
    {
        var monetPalette = context.MonetPalette;
        var monetColors = context.MonetColors?.Where(color => color.A > 0).ToArray() ?? [];
        var primary = context.UseNeutralSurfaces
            ? context.AccentColor
            : monetPalette?.Primary ?? (monetColors.Length > 0 ? monetColors[0] : context.AccentColor);
        var secondary = monetPalette?.Secondary
            ?? (monetColors.Length > 1
                ? monetColors[1]
                : ColorMath.Blend(primary, Color.Parse("#FFFFFFFF"), 0.14));
        var neutralPrimary = monetPalette?.Neutral
            ?? (monetColors.Length > 3
                ? monetColors[3]
                : ResolveNeutralBase(context.IsNightMode, role));
        var neutralSecondary = monetPalette?.NeutralVariant
            ?? (monetColors.Length > 4
                ? monetColors[4]
                : ResolveLiftBase(context.IsNightMode, role));
        var materialMode = ThemeAppearanceValues.ResolveEffectiveSystemMaterialMode(context.SystemMaterialMode);

        var (tintStrength, liftStrength, alpha, blurRadius) = ResolveModeParameters(materialMode, role, context.IsNightMode);
        var neutralBase = ResolveNeutralBase(context.IsNightMode, role);
        var neutralLift = ResolveLiftBase(context.IsNightMode, role);
        var isDockLike = role is MaterialSurfaceRole.DockBackground;
        var isComponentLike = role is MaterialSurfaceRole.DesktopComponentHost or MaterialSurfaceRole.StatusBarComponentHost;
        var baseMix = isDockLike ? 0.88 : isComponentLike ? 0.74 : 0.82;
        var liftMix = isDockLike ? 0.58 : isComponentLike ? 0.34 : 0.46;
        var neutralMix = isDockLike ? 0.22 : 0.16;

        var background = ColorMath.Blend(neutralBase, neutralPrimary, baseMix);
        background = ColorMath.Blend(background, neutralLift, liftMix);
        background = ColorMath.Blend(background, neutralSecondary, neutralMix);
        if (!context.UseNeutralSurfaces)
        {
            background = ColorMath.Blend(background, primary, tintStrength);
            background = ColorMath.Blend(background, secondary, liftStrength);
        }

        if (isDockLike && !context.IsNightMode)
        {
            background = ColorMath.Blend(background, Color.Parse("#FFFFFFFF"), 0.12);
        }

        background = Color.FromArgb(alpha, background.R, background.G, background.B);

        var borderSeed = context.IsNightMode
            ? ColorMath.Blend(neutralSecondary, Color.Parse("#FFFFFFFF"), 0.16)
            : ColorMath.Blend(neutralSecondary, Color.Parse("#FF334155"), 0.08);
        if (!context.UseNeutralSurfaces && !isComponentLike)
        {
            borderSeed = ColorMath.Blend(borderSeed, primary, 0.08);
        }

        var borderAlpha = role switch
        {
            MaterialSurfaceRole.DockBackground => context.IsNightMode ? (byte)0x34 : (byte)0x18,
            MaterialSurfaceRole.DesktopComponentHost or MaterialSurfaceRole.StatusBarComponentHost =>
                context.IsNightMode ? (byte)0x18 : (byte)0x10,
            MaterialSurfaceRole.StatusBarBackground => (byte)0x00,
            _ => context.IsNightMode ? (byte)0x26 : (byte)0x16
        };
        var border = ColorMath.WithAlpha(borderSeed, borderAlpha);

        return new AppearanceMaterialSurface(background, border, blurRadius, 1.0);
    }

    private static (double TintStrength, double LiftStrength, byte Alpha, double BlurRadius) ResolveModeParameters(
        string materialMode,
        MaterialSurfaceRole role,
        bool isNightMode)
    {
        if (role == MaterialSurfaceRole.SettingsWindowBackground)
        {
            return materialMode switch
            {
                ThemeAppearanceValues.MaterialAcrylic => (
                    0.20,
                    0.14,
                    isNightMode ? (byte)0x8E : (byte)0x96,
                    0),
                ThemeAppearanceValues.MaterialMica => (
                    0.14,
                    0.08,
                    isNightMode ? (byte)0x9E : (byte)0xA6,
                    0),
                _ => (0.08, 0.05, (byte)0xFF, 0)
            };
        }

        var isOverlay = role is MaterialSurfaceRole.DockBackground or MaterialSurfaceRole.StatusBarBackground or MaterialSurfaceRole.OverlayPanel;
        return materialMode switch
        {
            ThemeAppearanceValues.MaterialAcrylic => (
                isOverlay ? 0.30 : 0.20,
                isOverlay ? 0.22 : 0.14,
                isNightMode ? (byte)0xD8 : (byte)0xE0,
                isOverlay ? 36 : 28),
            ThemeAppearanceValues.MaterialMica => (
                isOverlay ? 0.20 : 0.14,
                isOverlay ? 0.12 : 0.08,
                isNightMode ? (byte)0xEC : (byte)0xF2,
                isOverlay ? 28 : 20),
            _ => (
                isOverlay ? 0.12 : 0.08,
                isOverlay ? 0.08 : 0.05,
                (byte)0xFF,
                0)
        };
    }

    private static Color ResolveNeutralBase(bool isNightMode, MaterialSurfaceRole role)
    {
        return role switch
        {
            MaterialSurfaceRole.WindowBackground => isNightMode ? Color.Parse("#FF0A0F16") : Color.Parse("#FFF7F8FA"),
            MaterialSurfaceRole.SettingsWindowBackground => isNightMode ? Color.Parse("#FF0C121A") : Color.Parse("#FFF8FAFC"),
            MaterialSurfaceRole.DockBackground => isNightMode ? Color.Parse("#FF111A24") : Color.Parse("#FFFAFBFD"),
            MaterialSurfaceRole.StatusBarBackground => isNightMode ? Color.Parse("#FF101720") : Color.Parse("#FFF9FBFE"),
            MaterialSurfaceRole.StatusBarComponentHost => isNightMode ? Color.Parse("#FF111A23") : Color.Parse("#FFFCFDFE"),
            MaterialSurfaceRole.OverlayPanel => isNightMode ? Color.Parse("#FF131C27") : Color.Parse("#FFF4F7FB"),
            _ => isNightMode ? Color.Parse("#FF121B26") : Color.Parse("#FFFDFEFF")
        };
    }

    private static Color ResolveLiftBase(bool isNightMode, MaterialSurfaceRole role)
    {
        return role switch
        {
            MaterialSurfaceRole.DockBackground or MaterialSurfaceRole.StatusBarBackground or MaterialSurfaceRole.OverlayPanel =>
                isNightMode ? Color.Parse("#FF1B2633") : Color.Parse("#FFFFFFFF"),
            _ => isNightMode ? Color.Parse("#FF17212D") : Color.Parse("#FFFFFFFF")
        };
    }
}
