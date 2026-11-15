using System;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using MaterialColorUtilities.Palettes;
using MaterialColorUtilities.Utils;
using TKCode.Arc;

namespace LanMountainDesktop.Services;

/// <summary>
/// 双轨桥接的 Arc 侧（docs/ARC-MIGRATION-PLAN.md §2）：把壁纸取色快照灌进 Arc 设计令牌，
/// 让 Arc 控件与 widget 的 Adaptive*/ComponentRoleBrushes 共用同一条 Monet 真理来源。
///
/// 输入语义对齐 MonetColorService.BuildPaletteCore（TonalSpot、Accent = Primary 40/80）；
/// 色彩转换复用 MonetColorService（不另起一份）。Arc 的 token 覆写在明暗切换后不会自动重算
/// （官方文档警告，P0 已实测确认），因此每次明暗变化都整组重放。
/// </summary>
internal static class ArcThemeBridge
{
    // 与 App.axaml 的 AppFontFamily 同源：正文令牌跟随应用字体（MiSans）
    private const string BodyFontFamilyToken =
        "MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans, avares://LanMountainDesktop/Assets/Fonts#MiSans VF";

    private static bool _attached;

    public static void Attach(IMaterialColorService service)
    {
        if (_attached || service is null)
        {
            return;
        }

        _attached = true;
        service.MaterialColorChanged += OnBridgeMaterialColorChanged;
        if (service is MaterialColorService concrete)
        {
            concrete.AppearanceThemeChanged += (_, _) => ApplyArcTheme(service.GetMaterialColorSnapshot());
        }

        ApplyArcTheme(service.GetMaterialColorSnapshot());
    }

    private static void OnBridgeMaterialColorChanged(object? sender, MaterialColorSnapshot snapshot)
        => ApplyArcTheme(snapshot);

    internal static void ApplyArcTheme(MaterialColorSnapshot snapshot)
    {
        var seed = snapshot.EffectiveSeedColor;
        var dark = snapshot.IsNightMode;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyArcThemeCore(seed, dark));
            return;
        }

        ApplyArcThemeCore(seed, dark);
    }

    private static void ApplyArcThemeCore(Color seed, bool dark)
    {
        try
        {
            var core = CorePalette.Of(MonetColorService.ToArgb(seed), Style.TonalSpot);
            var accent = MonetColorService.FromArgb(core.Primary.Tone(dark ? 80u : 40u));

            ArcTheme.Apply(t => t
                .Appearance(dark ? ArcAppearance.Dark : ArcAppearance.Light)
                .Accent(accent)
                .Token("BodyFontFamily", BodyFontFamilyToken)
                // 表面（MD3 surface ladder，随种子带轻微色偏）
                .Token("Background", Tone(core.Neutral, dark ? 10u : 98u))
                .Token("Surface", Tone(core.Neutral, dark ? 14u : 95u))
                .Token("SurfaceMuted", Tone(core.Neutral, dark ? 6u : 92u))
                .Token("SurfaceRaised", Tone(core.Neutral, dark ? 18u : 99u))
                // 文字三级
                .Token("Foreground", Tone(core.Neutral, dark ? 93u : 12u))
                .Token("TextSecondary", Tone(core.NeutralVariant, dark ? 80u : 30u))
                .Token("TextMuted", Tone(core.NeutralVariant, dark ? 60u : 50u))
                // 边框三级
                .Token("Border", Tone(core.NeutralVariant, dark ? 30u : 80u))
                .Token("BorderSubtle", Tone(core.NeutralVariant, dark ? 22u : 88u))
                // 强调色族（MD3 容器语义）
                .Token("AccentStrong", Tone(core.Primary, dark ? 90u : 30u))
                .Token("AccentSubtle", Tone(core.Primary, dark ? 30u : 90u))
                .Token("AccentForeground", Tone(core.Primary, dark ? 20u : 100u)));
        }
        catch (Exception ex)
        {
            // 桥接失败不阻断启动/换壁纸：Arc 控件回落到 Arc 默认主题
            AppLogger.Warn("ArcTheme", "Failed to apply Arc theme bridge.", ex);
        }
    }

    private static string Tone(TonalPalette palette, uint tone)
        => FromArgb(palette.Tone(tone));

    private static string FromArgb(uint argb)
    {
        var r = (byte)((argb >> 16) & 0xFF);
        var g = (byte)((argb >> 8) & 0xFF);
        var b = (byte)(argb & 0xFF);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
