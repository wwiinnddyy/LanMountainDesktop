using System;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Headless.XUnit;

using LanMountainDesktop.Theme;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 未挂树的控件查不查得到 <c>Adaptive*</c> 键——烧色值那一族的时机断言全都建在这件事上，所以它得钉住。
///
/// 2026-10-01 第十五笔（计时器四个数字，b3ed392）先给出过一个<b>错解释</b>：
/// 那次给 attach 路径加了一行强制重画，理由是"底座在应用级注册了键，未挂树也查得到，
/// 所以夹具看不见构造期取色落灰"（生产里 <c>App.axaml.cs:1153</c> 确实把主题键注册进 Application 自家资源）。
/// 删掉那行后两格照样绿，我据此把"看不见"当成了底座的性质。实测把这条推翻：
/// 应用级查到 4/4 支（<c>AdaptiveTextPrimaryBrush=#ff0b1220</c>、<c>AdaptiveSurfaceRaisedBrush=White</c> 这类默认档），
/// 而一个没挂树、自家也没资源的 <c>Border</c> 查到 <b>0/4</b>——查找链并不落到应用级。
///
/// 两格合起来的意思，也是后面每一笔要用的口径：
/// ① 构造期取色<b>一定</b>拿不到键、只会拿到调用方给的兜底 → 时机缺陷在这套夹具里<b>可观察</b>，
///    "挂树之后又画了一次"才是那四格数字真绿的原因（这一点另由 <c>TimerNumberColumnThemeTests</c> 钉着）；
/// ② 挂上去的控件若所在作用域没注册主题资源，会静默落到应用级那套默认档（白底黑字），
///    所以"绿"不等于"接的是主题层算出来的那支"——比色必须比 <c>Registered(host, key)</c>，不能比硬编码值。
/// </summary>
public sealed class ThemeKeyScopeLayerProbeTests
{
    private static readonly string[] ProbedKeys =
    [
        ThemeResourceKeys.TextPrimaryBrush,
        ThemeResourceKeys.TextSecondaryBrush,
        ThemeResourceKeys.TextMutedBrush,
        ThemeResourceKeys.SurfaceRaisedBrush,
    ];

    [AvaloniaFact]
    public void UnattachedControl_CannotResolveAdaptiveKeys_AppScopeFallbackIsNotInvisible()
    {
        var application = Avalonia.Application.Current;
        Assert.NotNull(application);

        var fromApp = LookupKeys(application!);
        var unattached = LookupKeys(new Border());

        Assert.True(
            unattached.Count == 0,
            $"未挂树的控件查到了 {unattached.Count}/{ProbedKeys.Length} 支键（{string.Join(" / ", unattached)}）" +
            "——这条一旦不再是 0，所有『构造期取色会落兜底』的推理都要重量：" +
            "意味着查找链开始落到应用级，未挂树也能取到色，时机类断言就失去效力了");

        Assert.True(
            fromApp.Count == ProbedKeys.Length,
            $"应用级只查到 {fromApp.Count}/{ProbedKeys.Length} 支（{string.Join(" / ", fromApp)}）" +
            "——应用级那套默认档是挂错作用域时的静默落点，缺键会让『接错档要红』的格变成假绿");

        // 两层的差别就是这条判据的意义：应用级有值、未挂树取不到值。
        Assert.True(
            fromApp.Count > unattached.Count,
            $"应用级 {fromApp.Count} 支 / 未挂树 {unattached.Count} 支——层级差没了，本文件头注那两条口径都得重读");
    }

    private static List<string> LookupKeys(IResourceHost host)
    {
        var found = new List<string>();
        foreach (var key in ProbedKeys)
        {
            if (AdaptiveTokens.TryGet<IBrush>(host, key, out var brush) && brush is ISolidColorBrush solid)
            {
                found.Add($"{key}={solid.Color}");
            }
        }

        return found;
    }
}
