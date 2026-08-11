using System.Collections.Generic;
using Avalonia.Media;
using LanMountainDesktop.Services;
using LanMountainDesktop.Shared.Contracts;

namespace LanMountainDesktop.Models;

public enum MaterialColorSourceKind
{
    Neutral = 0,
    CustomSeed = 1,
    WallpaperAuto = 2,
    AppWallpaper = 3,
    SystemWallpaper = 4,
    Fallback = 5
}

public sealed record MaterialColorPalette(
    Color Primary,
    Color Secondary,
    Color Accent,
    Color OnAccent,
    Color AccentLight1,
    Color AccentLight2,
    Color AccentLight3,
    Color AccentDark1,
    Color AccentDark2,
    Color AccentDark3,
    Color SurfaceBase,
    Color SurfaceRaised,
    Color SurfaceOverlay,
    Color TextPrimary,
    Color TextSecondary,
    Color TextMuted,
    Color TextAccent,
    Color NavText,
    Color NavSelectedText,
    Color NavSelectionIndicator,
    Color NavItemBackground,
    Color NavItemHoverBackground,
    Color NavItemSelectedBackground,
    Color ToggleOn,
    Color ToggleOff,
    Color ToggleBorder);

public sealed record MaterialSurfaceSnapshot(
    MaterialSurfaceRole Role,
    Color BackgroundColor,
    Color BorderColor,
    double BlurRadius,
    double Opacity);

public sealed record MaterialColorSnapshot(
    bool IsNightMode,
    string ThemeColorMode,
    string ThemeWallpaperColorSource,
    MaterialColorSourceKind ColorSourceKind,
    string ResolvedSeedSource,
    AppearanceCornerRadiusTokens CornerRadiusTokens,
    string? UserThemeColor,
    string? SelectedWallpaperSeed,
    Color EffectiveSeedColor,
    Color AccentColor,
    MonetPalette MonetPalette,
    MaterialColorPalette Palette,
    IReadOnlyList<Color> WallpaperSeedCandidates,
    string SystemMaterialMode,
    IReadOnlyList<string> AvailableSystemMaterialModes,
    bool CanChangeSystemMaterial,
    bool UseSystemChrome,
    string? ResolvedWallpaperPath,
    bool UseNativeWallpaperChangeEvents,
    bool NativeWallpaperChangeEventsActive,
    bool WallpaperPollingActive,
    IReadOnlyDictionary<MaterialSurfaceRole, MaterialSurfaceSnapshot> Surfaces);
