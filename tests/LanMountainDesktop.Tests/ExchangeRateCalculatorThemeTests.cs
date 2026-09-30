using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
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
/// 汇率计算器这块面板"跟不跟主题走"的钉。2026-09-30 之前它不跟：根卡片早就走主题，
/// 里面 16 处文字与控件底还是写死的白天值——本主题夜档卡底是 <c>#FF131922</c>
/// （<c>ThemeColorSystemService.cs:99</c>），而正文写的是 <c>#121722</c>，实测对比度 <b>1.02:1</b>，
/// 也就是黑夜档下这块面板基本读不出来。这一格守的就是"别再退回写死"。
///
/// 只开一个用例、内部逐档循环（#20 / #G1-I：每多一格 Avalonia 用例就多一次会话参与）。
/// 对比度一律按<b>实际叠出来的那一层底</b>算：文字压在控件底（overlay，自带 α 0xE8/0xF2）上，
/// 控件底又压在卡片底（raised）上，所以两层都合成人眼看到的那个不透明色再比
/// （<c>ColorMath.ToOpaqueAgainst</c>——"比观感必须先把 α 算进去"那条同族教训）。
/// </summary>
public sealed class ExchangeRateCalculatorThemeTests
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

    /// <summary>(元素名, 它压在哪一层底上, 该角色的对比度门槛, 改之前那个写死值)。</summary>
    private static readonly (string Name, string Backing, double Floor, string OldLiteral)[] TextCells =
    [
        ("FromCurrencyCodeTextBlock", "chip", 4.5, "#FF121722"),
        ("ToCurrencyCodeTextBlock", "chip", 4.5, "#FF121722"),
        ("ConvertedAmountTextBlock", "chip", 4.5, "#FF0F1622"),
        ("FromCurrencyNameTextBlock", "chip", 3.0, "#FF6C7382"),
        ("ToCurrencyNameTextBlock", "chip", 3.0, "#FF6C7382"),
        ("RateTextBlock", "card", 3.0, "#FF646D7D"),
        ("StatusTextBlock", "card", 3.0, "#FF5E6677"),
    ];

    [AvaloniaFact]
    public void TextAndChips_FollowTheThemeAndStayReadable()
    {
        var failures = new List<string>();

        foreach (var sample in new (bool Night, ThemeColorContext Context)[] { (false, DayContext), (true, NightContext) })
        {
            var mode = sample.Night ? "夜" : "昼";
            var widget = new ExchangeRateCalculatorWidget();
            var host = new Border { Child = widget };
            ThemeColorSystemService.ApplyThemeResources(host.Resources, sample.Context);

            // 必须先上屏一次：`new` 出来的 UserControl 在布局之前可视树里一个子节点都没有
            // （实测 controls=0，害我先怀疑自己的改动）。
            var window = new Window { Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var card = SolidColor(widget, "RootBorder", "Background");
            var chip = SolidColor(widget, "FromCurrencyRowBorder", "Background");
            if (card is null || chip is null)
            {
                var controls = widget.GetVisualDescendants().OfType<Control>().ToList();
                failures.Add(
                    $"{mode}档：卡片底或货币行底没解析到纯色（card={card} chip={chip}，控件数={controls.Count}）" +
                    "——DynamicResource 没解出来、树没起来，还是那一行被改回字面量？");
                window.Close();
                Dispatcher.UIThread.RunJobs();
                continue;
            }

            // 控件底自带 α，先合成到卡片上，才是文字真正压在的那一层。
            var chipOnCard = ColorMath.ToOpaqueAgainst(chip.Value, card.Value);

            foreach (var (name, backing, floor, oldLiteral) in TextCells)
            {
                var paint = SolidColor(widget, name, "Foreground");
                if (paint is null)
                {
                    failures.Add($"{mode}档：{name} 的 Foreground 没解析到纯色");
                    continue;
                }

                if (string.Equals(paint.Value.ToString(), oldLiteral, StringComparison.OrdinalIgnoreCase))
                {
                    // 大小写不敏感是实测补上的：Avalonia 的 Color.ToString() 出的是小写十六进制，
                    // 按相等比就永远走不到这一格，那条"还是写死的"消息会变成死代码。
                    failures.Add($"{mode}档：{name} 还是写死的 {oldLiteral}——这一行没跟着主题走");
                    continue;
                }

                var behind = backing == "chip" ? chipOnCard : card.Value;
                var ink = ColorMath.ToOpaqueAgainst(paint.Value, behind);
                var ratio = ColorMath.ContrastRatio(ink, behind);
                if (ratio < floor)
                {
                    failures.Add(
                        $"{mode}档：{name} 对比度 {ratio:F2}:1 < 门槛 {floor:0.0}:1（字 {ink} 压在 {behind} 上）");
                }
            }

            // 键盘那 15 个按钮的颜色来自 <Style>/<Setter Value="{DynamicResource …}">。
            // 这一格同时钉两件事：Setter 里 DynamicResource 真的会解析（2026-09-30 实测之前这一点是"未查证"，
            // 一度据此不动那三条字面量），以及接的是哪两个键——接错档（比如把控件底接成正文色）要红。
            // 键盘数字键的颜色只可能来自 <Style>/<Setter Value="{DynamicResource …}">：
            // 它们自己没有元素级 Background/Foreground（`SwapCurrencyButton` 有，所以必须按"内容是数字"挑，
            // 否则取到换币键、钉的就又是元素属性那一行——第一版就是这么假绿的）。
            var keypad = widget.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Content is string digits
                    && digits.Length == 1
                    && char.IsAsciiDigit(digits[0]));
            if (keypad is null)
            {
                failures.Add($"{mode}档：可视树里找不到数字键，Setter 那一族没验到");
            }
            else
            {
                var overlayKey = Registered(host, ThemeResourceKeys.SurfaceOverlayBrush);
                var primary = Registered(host, ThemeResourceKeys.TextPrimaryBrush);
                var keypadBack = (keypad.Background as ISolidColorBrush)?.Color;
                var keypadInk = (keypad.Foreground as ISolidColorBrush)?.Color;
                if (overlayKey is null || primary is null || keypadBack != overlayKey || keypadInk != primary)
                {
                    failures.Add(
                        $"{mode}档：键盘按钮没接上 Setter 该接的两档" +
                        $"（底 {keypadBack} 应为 {overlayKey}；字 {keypadInk} 应为 {primary}）");
                }
            }

            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>主题在那一层作用域上注册的值本身——"组件拿到的 == 注册的那支"，接错档才会红。</summary>
    private static Color? Registered(IResourceHost host, string key) =>
        AdaptiveTokens.TryGet<IBrush>(host, key, out var brush) && brush is ISolidColorBrush solid
            ? solid.Color
            : null;

    /// <summary>沿可视树按 x:Name 找控件，取那个属性的纯色；没命名、不是纯色或缺键都回 null。</summary>
    private static Color? SolidColor(Avalonia.Visual root, string name, string property)
    {
        var target = root.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control => string.Equals(control.Name, name, StringComparison.Ordinal));
        if (target is null)
        {
            return null;
        }

        var brush = property switch
        {
            "Background" => (target as Border)?.Background,
            "Foreground" => (target as TextBlock)?.Foreground,
            _ => null,
        };

        return brush is ISolidColorBrush solid ? solid.Color : null;
    }
}
