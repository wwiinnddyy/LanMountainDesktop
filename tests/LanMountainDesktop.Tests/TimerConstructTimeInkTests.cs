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
/// 构造期取色这件事，按 2026-10-01 实测钉成三条（#115 的一半；诊断跑出来的原始值写在每条消息里）。
///
/// ① 未挂树的控件算出来的黑夜档是<b>昼</b>（判据：`CenterDivider` 停在昼档字面量，而不是夜档），
///    同时角色画笔那几格一定是 `ComponentRoleBrushes` 的中性灰（没资源作用域可查）。
/// ② 挂进<b>昼</b>档窗口后，角色那几格离开灰、`CenterDivider` 却还是昼档那个值——
///    两边不一样这件事本身就是结论：<b>代码面没有重画</b>（`RefreshNightVisualIfChanged`
///    见黑夜值没变就早退，ComponentThemeMode.cs:52-55），把字色换对的是<b>标记面</b>那行
///    `Foreground="{DynamicResource Adaptive…Brush}"` 自己重解析。
/// ③ 挂进<b>夜</b>档窗口后 `CenterDivider` 才变成夜档值——代码面只在"值真的不同"时才跑。
///
/// 为什么这三条要钉住（后面十几笔烧色值的口径就在这）：
/// 一格里"构造期落灰"能不能被救回，取决于<b>它有没有标记面的 DynamicResource</b>。
/// 只有代码面取色的那些格（动态造出来、或扇出到记录字段的那些）在<b>昼档</b>会一直停在灰——
/// 因为 ctor 判定恒为昼，早退把重画挡掉了。#G1-DA 的 DailyNewsView 量的正是这一种，
/// 所以那一笔带了一次 attach 时的无条件重画；计时器这一列因为有标记面兜着，不需要。
/// </summary>
public sealed class TimerConstructTimeInkTests
{
    private static readonly ThemeColorContext DayContext = new(
        Color.FromRgb(0x40, 0x80, 0xC0), IsLightBackground: true, IsLightNavBackground: true, IsNightMode: false);

    private static readonly ThemeColorContext NightContext = new(
        Color.FromRgb(0x40, 0x80, 0xC0), IsLightBackground: false, IsLightNavBackground: false, IsNightMode: true);

    // 计时器面板上那支分隔条的昼/夜字面量——本文件用它当"代码面这次到底跑没跑"的探针。
    private static readonly Color DayDivider = Color.Parse("#d5dae3");
    private static readonly Color NightDivider = Color.Parse("#434b5c");

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructTimeInk_IsRescuedByMarkupNotByTheCodePass(bool isNightMode)
    {
        var mode = isNightMode ? "夜" : "昼";
        var widget = new TimerWidget();

        var atCtor = Read(widget, logical: true);
        Assert.True(
            atCtor.Inks.All(v => v == Colors.Gray),
            $"①构造期四格不全是兜底灰（现量 {string.Join(" / ", atCtor.Inks)}）——" +
            "说明未挂树时已经查得到 Adaptive 键，那『构造期取色会落灰』这条前提要重读");
        Assert.Equal(DayDivider, atCtor.Divider);

        var host = new Border { Child = widget };
        ThemeColorSystemService.ApplyThemeResources(host.Resources, isNightMode ? NightContext : DayContext);
        var window = new Window { Content = host };
        window.RequestedThemeVariant = isNightMode ? ThemeVariant.Dark : ThemeVariant.Light;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var after = Read(widget, logical: false);

        // ②/③ 代码面跑没跑，看分隔条；标记面救不救，看那四格。
        if (isNightMode)
        {
            Assert.True(
                after.Divider == NightDivider,
                $"③{mode}档分隔条仍是 {after.Divider}——代码面这次没重画，夜档判定在挂树后没生效？");
        }
        else
        {
            Assert.True(
                after.Divider == DayDivider,
                $"②{mode}档分隔条变成 {after.Divider}（夜档是 {NightDivider}）——" +
                "昼档里代码面本应因早退而不重画，它跑了说明守卫变了，本文件的②要改写");
        }

        Assert.True(
            after.Inks.All(v => v != Colors.Gray),
            $"②/③{mode}档挂树后这四格还停在灰（{string.Join(" / ", after.Inks)}）——" +
            "标记面的 DynamicResource 没把这批字色救回来；那时这些格就得靠 attach 时的无条件重画");

        var registered = new Dictionary<string, Color?>(StringComparer.Ordinal)
        {
            ["MainNumberTextBlock"] = Registered(host, ThemeResourceKeys.TextPrimaryBrush),
            ["TopNumberTextBlock"] = Registered(host, ThemeResourceKeys.TextSecondaryBrush),
            ["NextNumberTextBlock"] = Registered(host, ThemeResourceKeys.TextSecondaryBrush),
            ["NextNextNumberTextBlock"] = Registered(host, ThemeResourceKeys.TextMutedBrush),
        };
        Assert.True(
            after.Pairs.All(kv => registered[kv.Key] is not null && kv.Value == registered[kv.Key]),
            $"{mode}档这四格与注册值不完全一致：" +
            string.Join(" / ", after.Pairs.Select(kv => $"{kv.Key}={kv.Value}(注册={registered[kv.Key]})")) +
            "——字色跟了别一档（比如应用级那套默认档），比色必须比 Registered(host, key)");

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static (List<Color> Inks, Color Divider, Dictionary<string, Color> Pairs) Read(Control root, bool logical)
    {
        var walk = logical
            ? root.GetLogicalDescendants().OfType<Control>()
            : root.GetVisualDescendants().OfType<Control>();
        var blocks = new[] { "MainNumberTextBlock", "TopNumberTextBlock", "NextNumberTextBlock", "NextNextNumberTextBlock" };
        var inks = new List<Color>();
        var pairs = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (var name in blocks)
        {
            var block = walk.OfType<TextBlock>().FirstOrDefault(t => t.Name == name);
            if (block?.Foreground is ISolidColorBrush solid)
            {
                inks.Add(solid.Color);
                pairs[name] = solid.Color;
            }
        }

        var divider = walk.OfType<Border>().FirstOrDefault(b => b.Name == "CenterDivider");
        var dividerColor = divider?.Background is ISolidColorBrush ds ? ds.Color : default;
        return (inks, dividerColor, pairs);
    }

    private static Color? Registered(IResourceHost host, string key) =>
        AdaptiveTokens.TryGet<IBrush>(host, key, out var brush) && brush is ISolidColorBrush solid
            ? solid.Color
            : null;
}
