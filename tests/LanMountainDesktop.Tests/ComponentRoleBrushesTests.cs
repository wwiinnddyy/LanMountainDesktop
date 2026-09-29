using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;

using Avalonia.Headless.XUnit;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件角色色的落点。已烧五族：状态文字（10 处）、正文（17 处）、次要文字与图标字形（17 处）、
/// 卡片与根面板底（9 处）、控件底（10 处里的 9 处）——原来各处都写着
/// <c>_isNightVisual ? 夜档 : 日档</c> 三元组：夜档每族内部一模一样
/// （<c>#8B95A5</c> / <c>#E8EAED</c> / <c>#A8B1C2</c> / <c>#1B2129</c> / <c>#2D3440</c>），
/// 日档却各自漂开（正文 6 个值、次要 11 个值、控件底 6 个值），
/// 现在一律走 <c>ComponentRoleBrushes</c> 问主题层要对应角色的那一支画笔。
///
/// 钉的是两件事，不是"等于某个十六进制"：**跟着注册值走**（键接错要红）与
/// **换壁纸/换档之后仍然读得清**（对比度达到该角色自己的门槛，或阶梯方向没反）。
/// 主题那些值是按 surfaceRaised 混色后再 EnsureContrast 算的
/// （<c>ThemeColorSystemService.cs:117-124</c>），而写死的三元组两件事都不保证——它就是绕过后者才存在的。
///
/// <b>为什么全类只开一个测试、内部逐档循环</b>：这是实测出来的取舍。原先写成
/// <c>[AvaloniaTheory]</c> 9 行（三角色 × 明暗 6 + 阶梯 2 + 同作用域 1），push 之后 Actions 三趟全红，
/// 每趟"失败数 == 线程归属失败数"，而受害者正是这些新用例：
/// <c>Test Case Cleanup Failure … different thread owns it</c>（就是 #G1-I）。
/// 每多一格 Avalonia 用例就多一次会话参与，而 #20 已量过"同形会话多开即复现"。
/// 合并成单会话后断言一条不少（每条失败都点名档位与角色），偶发面从 9 次降到 1 次。
/// </summary>
public sealed class ComponentRoleBrushesTests
{
    private static readonly ThemeColorContext DayContext = new(
        Color.FromRgb(0x40, 0x80, 0xC0),
        IsLightBackground: true,
        IsLightNavBackground: true,
        IsNightMode: false);

    private static readonly ThemeColorContext NightContext = new(
        Color.FromRgb(0x40, 0x80, 0xC0),
        IsLightBackground: false,
        IsLightNavBackground: false,
        IsNightMode: true);

    /// <summary>文字角色 → (键, 门槛)。门槛不是一条通用值：primary 生产按 4.5 造，另两档按 3。</summary>
    private static readonly (string Role, string Key, double Floor)[] TextRoles =
    [
        ("muted", ThemeResourceKeys.TextMutedBrush, 3.0),
        ("primary", ThemeResourceKeys.TextPrimaryBrush, 4.5),
        ("secondary", ThemeResourceKeys.TextSecondaryBrush, 3.0),
    ];

    [AvaloniaFact]
    public void RoleBrushes_FollowTheTheme_AndStayReadable_InBothModes()
    {
        var failures = new List<string>();

        foreach (var sample in new (bool Night, ThemeColorContext Context)[] { (false, DayContext), (true, NightContext) })
        {
            var mode = sample.Night ? "夜" : "昼";
            var host = ThemedHost(sample.Context);

            if (Registered(host, ThemeResourceKeys.SurfaceRaisedBrush) is not Color panel)
            {
                failures.Add($"{mode}档：主题没注册纯色 surfaceRaised，文字对比度无从判定");
                continue;
            }

            foreach (var role in TextRoles)
            {
                if (Resolved(host, role.Role) is not Color solid)
                {
                    failures.Add($"{mode}档 {role.Role}：拿到的不是纯色画笔");
                    continue;
                }

                if (Registered(host, role.Key) is not Color registered)
                {
                    failures.Add($"{mode}档 {role.Role}：主题没注册 {role.Key}，该角色所有调用点会一起落到中性灰");
                    continue;
                }

                if (solid != registered)
                {
                    failures.Add($"{mode}档 {role.Role}：取到的不是 {role.Key} 注册的那支"
                        + $"（{solid} vs {registered}）——家里的键接错了");
                    continue;
                }

                var contrast = ColorMath.ContrastRatio(solid, panel);
                if (contrast < role.Floor)
                {
                    failures.Add($"{mode}档 {role.Role} 对面板底色只有 {contrast:F2}:1，低于 {role.Floor}:1"
                        + "——烧成 token 的全部理由就是这条保证不再由组件自己绕过");
                }
            }

            // 两层底的阶梯方向：夜里控件底比面板底亮、白天比它暗。组件写死的卡片『#1B2129』与
            // 控件底『#2D3440』编码的就是这条相对关系；保住方向就够了，值本该随壁纸漂，所以不钉像素。
            // 必须**先合成再比**：主题的 surfaceOverlay 自带 α（0xE8 / 0xF2），
            // 拿不含 α 的 RGB 亮度判方向会高估那一层——看着更亮、压到卡片上其实没那么亮。
            var card = Resolved(host, "card");
            var chip = Resolved(host, "chip");
            if (card is null || chip is null)
            {
                failures.Add($"{mode}档：卡片底或控件底没拿到纯色");
                continue;
            }

            var cardLum = ColorMath.RelativeLuminance(card.Value);
            var chipLum = ColorMath.RelativeLuminance(ColorMath.ToOpaqueAgainst(chip.Value, card.Value));
            var ladderHolds = sample.Night ? chipLum > cardLum : chipLum < cardLum;
            if (!ladderHolds)
            {
                failures.Add($"{mode}档阶梯反了：控件底合成到卡片底之后 {chipLum:F3} 对卡片 {cardLum:F3}"
                    + $"（应{(sample.Night ? "更亮" : "更暗")}）——控件与面板会糊成一片");
            }
        }

        // 同一作用域里两个控件必须拿到同一支颜色：家若退回"只看自己那本子字典"（TryGetResource 那个坑），
        // 没上屏的控件会各自失色——这里正是"没上屏"的形状。
        {
            var first = new TextBlock();
            var second = new TextBlock();
            var scoped = new Border { Resources = new ResourceDictionary() };
            ThemeColorSystemService.ApplyThemeResources(scoped.Resources, DayContext);
            scoped.Child = new StackPanel { Children = { first, second } };

            var left = ResolvedFrom(ComponentRoleBrushes.MutedText(first));
            var right = ResolvedFrom(ComponentRoleBrushes.MutedText(second));
            if (left is null || right is null || left != right)
            {
                failures.Add($"同作用域两个控件拿到不同颜色（{left} vs {right}）");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>组件侧取到的颜色（角色名 → 家的方法），取不到或不是纯色返回 null。</summary>
    private static Color? Resolved(IResourceHost host, string role) => ResolvedFrom(role switch
    {
        "muted" => ComponentRoleBrushes.MutedText(host),
        "primary" => ComponentRoleBrushes.PrimaryText(host),
        "secondary" => ComponentRoleBrushes.SecondaryText(host),
        "card" => ComponentRoleBrushes.RaisedSurface(host),
        "chip" => ComponentRoleBrushes.OverlaySurface(host),
        _ => throw new ArgumentException($"未知的角色名 {role}", nameof(role)),
    });

    private static Color? ResolvedFrom(IBrush brush) => brush is ISolidColorBrush solid ? solid.Color : null;

    /// <summary>注册值本身。断言"组件拿到的 == 这个键注册的"，接错键才会红，而不是"亮度碰巧对"。</summary>
    private static Color? Registered(IResourceHost host, string key) =>
        AdaptiveTokens.TryGet<IBrush>(host, key, out var brush) ? ResolvedFrom(brush) : null;

    /// <summary>
    /// 带着主题资源、但没上屏的宿主：查找走的还是生产那条作用域链，只是不开窗口
    /// （<c>Window</c> + <c>Show()</c> 要多占一份合成器，正是要避开的东西）。
    /// </summary>
    private static Border ThemedHost(ThemeColorContext context)
    {
        var host = new Border();
        ThemeColorSystemService.ApplyThemeResources(host.Resources, context);
        return host;
    }
}
