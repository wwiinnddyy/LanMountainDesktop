using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

internal static class PluginAppearanceSnapshotMapper
{
    public static PluginAppearanceSnapshot FromMaterialColorSnapshot(MaterialColorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new PluginAppearanceSnapshot(
            PluginCornerRadiusTokens.FromShared(snapshot.CornerRadiusTokens),
            snapshot.IsNightMode ? "Dark" : "Light",
            ToText(snapshot.AccentColor),
            ToText(snapshot.EffectiveSeedColor),
            snapshot.ColorSourceKind.ToString(),
            snapshot.SystemMaterialMode,
            BuildColorRoles(snapshot),
            snapshot.Surfaces.ToDictionary(
                pair => pair.Key.ToString(),
                pair => new PluginMaterialSurfaceSnapshot(
                    ToText(pair.Value.BackgroundColor),
                    ToText(pair.Value.BorderColor),
                    pair.Value.BlurRadius,
                    pair.Value.Opacity),
                StringComparer.OrdinalIgnoreCase),
            snapshot.WallpaperSeedCandidates.Select(ToText).ToArray());
    }

    public static PluginAppearanceSnapshot FromAppearanceSnapshot(AppearanceThemeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new PluginAppearanceSnapshot(
            PluginCornerRadiusTokens.FromShared(snapshot.CornerRadiusTokens),
            snapshot.IsNightMode ? "Dark" : "Light",
            ToText(snapshot.AccentColor),
            ToText(snapshot.EffectiveSeedColor),
            snapshot.ResolvedSeedSource,
            snapshot.SystemMaterialMode,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["primary"] = ToText(snapshot.MonetPalette.Primary),
                ["secondary"] = ToText(snapshot.MonetPalette.Secondary),
                ["tertiary"] = ToText(snapshot.MonetPalette.Tertiary),
                ["neutral"] = ToText(snapshot.MonetPalette.Neutral),
                ["neutralVariant"] = ToText(snapshot.MonetPalette.NeutralVariant),
                ["accent"] = ToText(snapshot.AccentColor)
            },
            null,
            snapshot.WallpaperSeedCandidates.Select(ToText).ToArray());
    }

    private static IReadOnlyDictionary<string, string> BuildColorRoles(MaterialColorSnapshot snapshot)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["primary"] = ToText(snapshot.Palette.Primary),
            ["secondary"] = ToText(snapshot.Palette.Secondary),
            ["accent"] = ToText(snapshot.Palette.Accent),
            ["onAccent"] = ToText(snapshot.Palette.OnAccent),
            ["surfaceBase"] = ToText(snapshot.Palette.SurfaceBase),
            ["surfaceRaised"] = ToText(snapshot.Palette.SurfaceRaised),
            ["surfaceOverlay"] = ToText(snapshot.Palette.SurfaceOverlay),
            ["textPrimary"] = ToText(snapshot.Palette.TextPrimary),
            ["textSecondary"] = ToText(snapshot.Palette.TextSecondary),
            ["textMuted"] = ToText(snapshot.Palette.TextMuted),
            ["textAccent"] = ToText(snapshot.Palette.TextAccent),
            ["toggleOn"] = ToText(snapshot.Palette.ToggleOn),
            ["toggleOff"] = ToText(snapshot.Palette.ToggleOff),
            ["toggleBorder"] = ToText(snapshot.Palette.ToggleBorder)
        };
    }

    private static string ToText(Color color)
    {
        return color.ToString();
    }
}
