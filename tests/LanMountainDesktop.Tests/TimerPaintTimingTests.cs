using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 计时器那一列数字的<b>取色时机</b>钉（#114，2026-10-01）。
///
/// 起因是第十五笔（b3ed392）里对不上的一件事：把 attach 时的强制重画删掉，
/// <c>TimerNumberColumnThemeTests</c> 昼/夜两格照样绿；而 <c>ThemeKeyScopeLayerProbeTests</c>
/// 又量出未挂树的控件查不到任何 <c>Adaptive*</c> 键（0/4）。两条同时成立只有一个解释——
/// 构造期那一次着色拿到的是兜底灰，挂树之后<b>还有别的路径</b>又画了一次。
/// 这条路径以前只是推测，本文件把它钉成三件可观察的事：
///
/// ① 构造完但没挂树的控件，四个数字的字色就是 <c>ComponentRoleBrushes</c> 的那支中性灰
///    （<c>MutedTextFallback</c>，四格同一个值）——"取色太早"在夹具里本来就看得见；
/// ② 挂进注册了主题资源的窗口、跑一轮布局之后，四格都等于主题层注册的那一支（不是灰）；
/// ③ 从挂树到变对，中间真正触发重画的是 <c>SizeChanged → ApplyCellSize → ApplyModeVisualIfNeeded</c>
///    （<c>TimerWidget.axaml.cs:37</c> 只订了 SizeChanged，没订 ActualThemeVariantChanged；
///    构造期 <c>UpdateVisual</c> 已经调过一次 <c>ApplyModeVisualIfNeeded</c>，
///    若没有尺寸变化再叫一次，那层灰就不会被换掉——所以②成立本身就在证明③走过）。
///
/// 这一笔不新增任何产品行为：它只是把"时机安全是谁守着的"写清，供后面十几笔烧色值时引用。
/// </summary>
public sealed class TimerPaintTimingTests
{
    private static readonly ThemeColorContext DayContext = new(
        Color.FromRgb(0x40, 0x80, 0xC0),
        IsLightBackground: true,
        IsLightNavBackground: true,
        IsNightMode: false);

    private static readonly string[] CellNames =
    [
        "MainNumberTextBlock",
        "TopNumberTextBlock",
        "NextNumberTextBlock",
        "NextNextNumberTextBlock",
    ];

    [AvaloniaFact]
    public void NumberInk_IsFallbackGrayBeforeAttach_AndThemeInkAfterTheRealLayoutPass()
    {
        var widget = new TimerWidget();

        // ① 还没挂树：角色画笔查不到键，四格应当全是同一支中性灰。
        // 未挂树的 UserControl 在 Avalonia 12 下可视树是空的（GetVisualDescendants 回 0 个），
        // 所以构造期那一遍只能按逻辑树读——控件对象 InitializeComponent 就造好了，字色也已经涂过。
        var before = InkOf(widget, useVisualTree: false);
        Assert.True(
            before.Count == CellNames.Length,
            $"未挂树时按逻辑树只读到 {before.Count}/{CellNames.Length} 格的字色" +
            $"（{string.Join(" / ", before.Select(kv => $"{kv.Key}={kv.Value}"))}）" +
            "——读不全就判不了取色时机，两条遍历方向都试过再说结论");
        Assert.True(
            before.Values.All(c => c == Colors.Gray),
            "未挂树的四格并不全是兜底中性灰：" + string.Join(" / ", before.Select(kv => $"{kv.Key}={kv.Value}")) +
            "——说明控件在没有资源作用域时也能查到键，那『构造期取色会落灰』这条前提要重读" +
            "（与 ThemeKeyScopeLayerProbeTests 的 0/4 冲突，两边必须有一个改判）");

        // ② 挂进注册了主题资源的窗口，跑一轮真实布局（什么都不额外调用）。
        var host = new Border { Child = widget };
        ThemeColorSystemService.ApplyThemeResources(host.Resources, DayContext);
        var window = new Window { Content = host };
        window.RequestedThemeVariant = ThemeVariant.Light;

        var sizeChangedCount = 0;
        widget.SizeChanged += (_, _) => sizeChangedCount++;

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(
            sizeChangedCount > 0,
            "挂树后一次 SizeChanged 都没发生——那②若仍然变对，触发重画的就是别的路径，本文件的③要改写");

        var after = InkOf(widget, useVisualTree: true);
        var failures = new List<string>();
        foreach (var (role, key) in new (string, string)[]
        {
            ("MainNumberTextBlock", ThemeResourceKeys.TextPrimaryBrush),
            ("TopNumberTextBlock", ThemeResourceKeys.TextSecondaryBrush),
            ("NextNumberTextBlock", ThemeResourceKeys.TextSecondaryBrush),
            ("NextNextNumberTextBlock", ThemeResourceKeys.TextMutedBrush),
        })
        {
            var expected = Registered(host, key);
            if (expected is null)
            {
                failures.Add($"资源表里缺 {key}");
                continue;
            }

            if (!after.TryGetValue(role, out var ink))
            {
                failures.Add($"{role} 挂树后读不到纯色前景");
                continue;
            }

            if (ink == Colors.Gray)
            {
                failures.Add($"{role} 挂树后仍是兜底灰——构造期那次取色没人重画，取色时机缺陷是活的");
                continue;
            }

            if (ink != expected)
            {
                failures.Add($"{role} 挂树后是 {ink}，角色 {key} 注册的是 {expected}（接错档）");
            }
        }

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static Dictionary<string, Color> InkOf(Control root, bool useVisualTree)
    {
        var found = new Dictionary<string, Color>(StringComparer.Ordinal);
        var walk = useVisualTree
            ? root.GetVisualDescendants().OfType<TextBlock>()
            : root.GetLogicalDescendants().OfType<TextBlock>();
        foreach (var block in walk
                     .Where(t => CellNames.Contains(t.Name)))
        {
            if (block.Foreground is ISolidColorBrush solid)
            {
                found[block.Name] = solid.Color;
            }
        }

        return found;
    }

    private static Color? Registered(IResourceHost host, string key) =>
        AdaptiveTokens.TryGet<IBrush>(host, key, out var brush) && brush is ISolidColorBrush solid
            ? solid.Color
            : null;
}
