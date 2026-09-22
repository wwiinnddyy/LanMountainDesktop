using System;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Media;

using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 学习组件面板的取色底座。此前 7 个学习组件各自抄了同一份"面板底色怎么求",
/// 6 份抄了同一份"面板底色上有哪些可采样的替身色", 深色/浅色底衬各抄了 7 份。
/// 唯一保留的既有分歧是 <see cref="BuildSoftSamples"/>: 学习历史面板少一个采样、混合比例更弱,
/// 那是它自己的排版口径, 不是复制漂移。
/// </summary>
public static class StudyPanelPalette
{
    private static readonly Color FallbackPanelColor = Color.Parse("#FF1E293B");
    private static readonly Color White = Color.Parse("#FFFFFFFF");

    /// <summary>玻璃面板背后的两种衬底: 深色桌面与浅色桌面各一种。</summary>
    public static readonly Color Dark = Color.Parse("#FF0B1220");

    public static readonly Color Light = Color.Parse("#FFF1F5FA");

    /// <summary>
    /// 学习组件状态徽章的三种底色：绿=会话进行中或安静达标，蓝=实时监测，灰=功能停用/无数据。
    /// 此前这三个色值以 <c>Color.Parse</c> 字面量的形式在 7 个组件里写了 15 遍。
    /// </summary>
    public static readonly Color SuccessBadge = Color.Parse("#FF0F6B49");

    public static readonly Color RealtimeBadge = Color.Parse("#FF2F5DA8");

    public static readonly Color DisabledBadge = Color.Parse("#FF9AA0A6");

    /// <summary>
    /// 徽章那一小片彩色玻璃的不透明度分档：面板越亮，徽章要越实才压得住字。
    /// 默认档给模式/操作徽章，<c>StatusBadgeTiers</c> 是学习噪声曲线状态徽章那档更实的写法。
    /// </summary>
    public static readonly (byte Strong, byte Middle, byte Weak) DefaultBadgeTiers = (0xE2, 0xD8, 0xC8);

    public static readonly (byte Strong, byte Middle, byte Weak) StatusBadgeTiers = (0xE6, 0xDB, 0xCC);

    /// <summary>徽章描边：固定 59% 不透明白，压在两种衬底上都是同一层薄边。</summary>
    public static IBrush BadgeBorderBrush { get; } = new SolidColorBrush(Color.FromArgb(0x96, 0xFF, 0xFF, 0xFF));

    /// <summary>
    /// 面板压在深色衬底上时的亮度。徽章不透明度与内嵌卡片的深浅切换都以它为准——
    /// 基准选"深色衬底"而不是面板自身颜色，半透明玻璃在两种桌面上才会分档一致。
    /// </summary>
    public static double LuminanceOnDark(Color panelColor) =>
        ColorMath.RelativeLuminance(ColorMath.ToOpaqueAgainst(panelColor, Dark));

    /// <summary>面板是不是偏亮（>0.58 这一档在徽章与卡片两处共用，别再各写一遍）。</summary>
    public static bool IsBrightPanel(Color panelColor) => LuminanceOnDark(panelColor) > 0.58;

    /// <summary>
    /// 按面板亮度取徽章底色。此前这套阈值在 7 个学习组件里各抄了一份，两档不透明度差异还藏在复制里。
    /// </summary>
    public static Color ResolveBadgeColor(
        Color panelColor,
        Color baseColor,
        (byte Strong, byte Middle, byte Weak) tiers = default)
    {
        var (strong, middle, weak) = tiers == default ? DefaultBadgeTiers : tiers;
        var panelLuminance = LuminanceOnDark(panelColor);
        var badgeAlpha = panelLuminance > 0.58 ? strong : panelLuminance > 0.46 ? middle : weak;
        return Color.FromArgb(badgeAlpha, baseColor.R, baseColor.G, baseColor.B);
    }

    /// <summary>徽章上的字色：把徽章当成面板上的一层叠加，再按对比度阈值从候选里挑。</summary>
    public static IBrush ResolveBadgeForeground(
        Color panelColor,
        Color badgeColor,
        IReadOnlyList<Color> foregroundCandidates,
        double minContrast = 4.5d)
    {
        var badgeComposite = ColorMath.ToOpaqueAgainst(badgeColor, ColorMath.ToOpaqueAgainst(panelColor, Dark));
        return AdaptiveBrushFactory.Create([badgeComposite], foregroundCandidates, minContrast);
    }

    /// <summary>面板底色: 先看自己的实心背景刷, 再退到主题里的强玻璃背景, 最后兜底。</summary>
    public static Color Resolve(Control owner, IBrush? panelBackground)
    {
        if (panelBackground is ISolidColorBrush solidBackground)
        {
            return solidBackground.Color;
        }

        if (AdaptiveTokens.TryGet<ISolidColorBrush>(owner, ThemeResourceKeys.GlassStrongBackgroundBrush, out var solidBrush))
        {
            return solidBrush.Color;
        }

        return FallbackPanelColor;
    }

    /// <summary>在半透明面板上挑字色时, 要同时保证两种衬底和它们中间的采样点都读得清。</summary>
    public static IReadOnlyList<Color> BuildSamples(Color panelColor)
    {
        return new[]
        {
            ColorMath.ToOpaqueAgainst(panelColor, Dark),
            ColorMath.ToOpaqueAgainst(panelColor, Light),
            ColorMath.Blend(ColorMath.ToOpaqueAgainst(panelColor, Dark), Dark, 0.28),
            ColorMath.Blend(ColorMath.ToOpaqueAgainst(panelColor, Dark), White, 0.16),
            ColorMath.Blend(ColorMath.ToOpaqueAgainst(panelColor, Light), White, 0.08),
            ColorMath.Blend(ColorMath.ToOpaqueAgainst(panelColor, Light), Dark, 0.18),
        };
    }

    /// <summary>学习历史面板的采样口径: 混合更弱、少一个采样点。</summary>
    public static IReadOnlyList<Color> BuildSoftSamples(Color panelColor)
    {
        return new[]
        {
            ColorMath.ToOpaqueAgainst(panelColor, Dark),
            ColorMath.ToOpaqueAgainst(panelColor, Light),
            ColorMath.Blend(ColorMath.ToOpaqueAgainst(panelColor, Dark), Dark, 0.22),
            ColorMath.Blend(ColorMath.ToOpaqueAgainst(panelColor, Dark), White, 0.14),
            ColorMath.Blend(ColorMath.ToOpaqueAgainst(panelColor, Light), White, 0.08),
        };
    }

    /// <summary>
    /// 模式角标的三件套一次涂完：底色、描边、前景字色。
    /// 此前 4 个学习组件各抄一遍这 4 行（差别只在前景候选色用哪张表），
    /// 改了描边常量而前景没跟着改是不报错的——症状是角标在浅面板上糊成一片。
    /// </summary>
    public static void ApplyModeBadge(
        Border badgeBorder,
        TextBlock badgeText,
        Color panelColor,
        Color baseColor,
        Color[] foregroundCandidates)
    {
        var badgeColor = ResolveBadgeColor(panelColor, baseColor);
        badgeBorder.Background = new SolidColorBrush(badgeColor);
        badgeBorder.BorderBrush = BadgeBorderBrush;
        badgeText.Foreground = ResolveBadgeForeground(panelColor, badgeColor, foregroundCandidates);
    }

}
