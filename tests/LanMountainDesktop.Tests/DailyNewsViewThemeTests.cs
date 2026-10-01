using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 每日新闻这块面板"跟不跟主题走"的钉（#G1-AF 第十二笔，2026-10-01）。
///
/// 它原来是一套**自带两档调色板**的界面：正文 <c>#e8e4e0/#34495e</c>、次要
/// <c>#9a9590/#757575</c>、面板底 <c>#3d3a3a/#f8f5ec</c>，同一份值在三个地方各抄一遍
/// （建条目的构造路径、建深度条目的 <c>CreateDetailedNewsPanel</c>、以及 <c>ApplyNightMode</c>
/// 那遍全树重画）。换壁纸时这块面板的底色与字色一动不动。
///
/// 第二组格钉的是**取色时机**，不是色值：角色画笔当场解析（<c>AdaptiveTokens.Brush</c> 走
/// <c>TryFindResource</c>），而宿主是 <c>new DailyNewsView(…)</c> 之后才把它加进面板
/// （JuyaNewsWidget.axaml.cs:506→511），构造期它不在任何资源作用域里，取到的只有兜底灰。
/// 所以这一格**按宿主的顺序**先建实例、再挂树。两件事都因此被钉住：
/// ① 把 <c>OnAttachedToVisualTree</c> 里那遍重画删掉 → 这里读到 <c>Gray</c>，红；
/// ② 把某一行的角色接错档（次要接成正文）→ 与注册值不等，红。
/// 只断"值来自主题"是测不出 ① 的——兜底灰也是一种"解析结果"。
/// </summary>
public sealed class DailyNewsViewThemeTests
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

    /// <summary>(元素文本, 期望的角色键, 该角色的对比度门槛)。</summary>
    private static readonly (string Text, string RoleKey, double Floor)[] InkCells =
    [
        ("•", ThemeResourceKeys.TextSecondaryBrush, 3.0),
        ("头条一条", ThemeResourceKeys.TextSecondaryBrush, 3.0),
        ("第二条带链接", ThemeResourceKeys.TextSecondaryBrush, 3.0),
        ("深度一条", ThemeResourceKeys.TextPrimaryBrush, 4.5),
        ("正文若干。", ThemeResourceKeys.TextPrimaryBrush, 4.5),
        // #G1-DA：这一格原先钉的是**现状**（写的是次要色、树遍历把它当标题行、实测拿到正文色），
        // 2026-10-01 把判据改成按 `Orientation: Horizontal` 认标题行之后，它才真的落回 Secondary。
        // 翻回 TextPrimaryBrush 就是那条遍历又认错了一类容器。
        ("相关链接：", ThemeResourceKeys.TextSecondaryBrush, 3.0),
    ];

    private static JuyaDailyNews SampleNews() => new(
        new DateTime(2026, 10, 1),
        "测试日报",
        // 封面留空：非空会让构造期起一趟真 HTTP（LoadCoverImageAsync），测试不许碰网。
        CoverImageUrl: "",
        IssueUrl: "",
        BilibiliUrl: "",
        YoutubeUrl: "",
        OverviewCategories:
        [
            new JuyaOverviewCategory("要闻", "📰",
            [
                new JuyaOverviewItem("头条一条", string.Empty, 7),
                new JuyaOverviewItem("第二条带链接", "https://example.invalid/b", null),
            ]),
        ],
        DetailedNews:
        [
            new JuyaDetailedNewsItem("深度一条", 3, "正文若干。", ["https://example.invalid/a"]),
        ],
        FetchedAt: DateTimeOffset.UnixEpoch);

    [AvaloniaFact]
    public void TextAndPanels_FollowTheThemeOnceAttached()
    {
        var failures = new List<string>();

        foreach (var (mode, context) in new (string Mode, ThemeColorContext Context)[]
            { ("昼", DayContext), ("夜", NightContext) })
        {
            var view = new DailyNewsView(SampleNews(), isNightMode: mode == "夜");

            var host = new Border { Child = view };
            ThemeColorSystemService.ApplyThemeResources(host.Resources, context);
            var raised = Registered(host, ThemeResourceKeys.SurfaceRaisedBrush);
            if (raised is null)
            {
                failures.Add($"{mode}档：主题资源表里没有 {ThemeResourceKeys.SurfaceRaisedBrush}，" +
                    "后面的对比度全没法算——先看 ThemeColorSystemService 还注册不注册这个键");
                continue;
            }

            // 宿主那一层卡片底：日期行与深度条目都直接压在它上面（它们自己那层是 Transparent）。
            host.Background = new SolidColorBrush(raised.Value);

            var window = new Window { Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            foreach (var name in new[] { "CoverImageBorder", "OverviewBorder" })
            {
                var panel = FindControl<Border>(view, name);
                var actual = panel?.Background is ISolidColorBrush solid ? solid.Color : (Color?)null;
                if (actual != raised)
                {
                    failures.Add($"{mode}档：{name} 的底是 {actual}，主题的 raised 是 {raised}——这块面板没接到主题档");
                }
            }

            foreach (var (text, roleKey, floor) in InkCells)
            {
                var element = FindByText(view, text);
                if (element is null)
                {
                    failures.Add($"{mode}档：可视树里找不到 {text}，夹具没建出来（这一格因此什么都没验）");
                    continue;
                }

                var ink = InkColor(element);
                var expectedBrush = Registered(host, roleKey);
                if (ink is null)
                {
                    failures.Add($"{mode}档：{text} 的前景色不是纯色（{element.GetType().Name}）");
                    continue;
                }

                if (expectedBrush is null)
                {
                    failures.Add($"{mode}档：资源表里缺 {roleKey}");
                    continue;
                }

                if (ink != expectedBrush)
                {
                    failures.Add(
                        $"{mode}档：{text} 的字色是 {ink}，角色 {roleKey} 注册的是 {expectedBrush}" +
                        "（差一个色档＝接错了角色；差得远＝构造期取色、落到了兜底灰）");
                    continue;
                }

                var behind = BackdropOf(element, raised.Value);
                var ratio = ColorMath.ContrastRatio(ColorMath.ToOpaqueAgainst(ink.Value, behind), behind);
                if (ratio < floor)
                {
                    failures.Add(
                        $"{mode}档：{text} 对比度 {ratio:F2}:1 < 门槛 {floor:0.0}:1（字 {ink} 压在 {behind} 上）");
                }
            }

            // #G1-DA 的另一半：那条认错容器的遍历在**定字号**那遍里也有。取 scale=2 才验得出第三个症状——
            // scale=1 时"从没被缩放的链接按钮"和"被正确缩放的链接按钮"都是 12，看不出来。
            // 三档在 scale=2 下应当是 标题 clamp(16·2)=20 / 正文 clamp(14·2)=16 / 「相关链接：」与链接=14。
            view.UpdateLayout(2.0, 600);
            foreach (var (text, expectedSize) in new (string, double)[]
                {
                    ("深度一条", 20.0),
                    ("正文若干。", 16.0),
                    ("相关链接：", 14.0),
                    ("https://example.invalid/a", 14.0),
                })
            {
                var target = FindByText(view, text);
                var size = FontSizeOf(target);
                if (target is null || size is null)
                {
                    failures.Add($"{mode}档：字号那一组找不到 {text}（或它没有字号），夹具没建出来");
                    continue;
                }

                if (Math.Abs(size.Value - expectedSize) > 0.01)
                {
                    failures.Add($"{mode}档：{text} 字号 {size}，应当是 {expectedSize}（scale=2）");
                }
            }

            // 品牌红是**故意不并**的：#d4736a/#bb5649 是这一组件的产品色（日期、展开按钮、#序号徽标、
            // 链接都用它），并到 TextAccent 等于替产品决定「新闻品牌色跟着壁纸变」。
            // 这一格钉的就是它没被顺手扫掉——与上面那组方向相反，所以两边都要有。
            var expectedBrand = mode == "夜" ? Color.Parse("#d4736a") : Color.Parse("#bb5649");
            var dateInk = InkColor(FindControl<TextBlock>(view, "DateTextBlock"));
            if (dateInk != expectedBrand)
            {
                failures.Add(
                    $"{mode}档：DateTextBlock 的字色是 {dateInk}，品牌色应当保持 {expectedBrand}" +
                    "（这一处是 #G1-AF 明写「不并」的产品色，被扫掉就是改错）");
            }

            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// 文字真正的底：往上找第一层有不透明纯色底的容器（概览卡/封面卡），
    /// 找不到就落宿主卡片那一层——Transparent 那层要跳过，不然比的是"没有底"。
    /// </summary>
    private static Color BackdropOf(Avalonia.Visual element, Color hostCard)
    {
        var current = element.GetVisualParent();
        while (current is not null)
        {
            if (current is Border border
                && border.Background is ISolidColorBrush solid
                && solid.Color.A == 0xFF)
            {
                return solid.Color;
            }

            current = current.GetVisualParent();
        }

        return hostCard;
    }

    private static Avalonia.Visual? FindByText(Avalonia.Visual root, string text) =>
        root.GetVisualDescendants().FirstOrDefault(visual => visual switch
        {
            TextBlock block => block.Text == text,
            ContentControl control when control.Content is string content => content == text,
            _ => false,
        });

    private static T? FindControl<T>(Avalonia.Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);

    private static Color? InkColor(Avalonia.Visual? element) => element switch
    {
        TextBlock block => (block.Foreground as ISolidColorBrush)?.Color,
        TemplatedControl control => (control.Foreground as ISolidColorBrush)?.Color,
        _ => null,
    };

    /// <summary>TextBlock 与 HyperlinkButton 这一类都要能读字号（前者不是 TemplatedControl，得分开认）。</summary>
    private static double? FontSizeOf(Avalonia.Visual? element) => element switch
    {
        TextBlock block => block.FontSize,
        TemplatedControl control => control.FontSize,
        _ => null,
    };

    /// <summary>主题在那一层作用域上注册的值本身——"组件拿到的 == 注册的那支"，接错档才会红。</summary>
    private static Color? Registered(IResourceHost host, string key) =>
        AdaptiveTokens.TryGet<IBrush>(host, key, out var brush) && brush is ISolidColorBrush solid
            ? solid.Color
            : null;
}
