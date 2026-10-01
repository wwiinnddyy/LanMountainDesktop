using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 扇出型双真源要有人把关（#G1-CN 的第二半）。
///
/// 现有那条 <see cref="DuplicatedPaintSourceRatchetTests"/> 按「x:Name + 属性名」配对，
/// 而组件常把标记里的**具名控件当构造实参**塞进一个视觉对象
/// （<c>new HotItemVisual(HotItem1Host, HotItem1Grid, HotItem1IndexTextBlock, HotItem1TextBlock)</c>），
/// 之后重画走字段名 <c>visual.IndexTextBlock.Foreground = …</c>——字段名≠x:Name，
/// 那条判据就以为标记那份没人覆盖。2026-10-01 用一份解名字的探针量出这一族共 24 处
/// （该探针先在"1 个字段扇出到 2 个具名控件"的手造样本上验过能报 2 处，才敢上真树）。
///
/// 三条一起钉，缺任何一条这个数都不可信：
/// ① **结果数等值**（不是"≤上限"）：写下这条时真树实测 16 处——24 里 Stcn24 的 8 处行板底
///    同日已烧（标记那份 #F7F8FA 改走 overlay 那一档的键），剩 8 处头像格（主题只有两层表面，
///    它要第三层，见 #G1-CY/#112）与 8 处品牌色（代码侧随莫奈档变）。等值比上限更早发现"尺子坏了"：
///    判据瞎了时上限照样绿。
/// ② **覆盖面下限**：能解析出"标记具名控件被当实参传进视觉对象"的构造站点 ≥16。
///    形参类型名单少列一项（探针第一版就少了 <c>Grid</c>）会让整族因"实参数≠形参数"被跳过，
///    于是报出一个**看起来像"已经收干净"的 0**。
/// ③ **四层计数随失败信息一起打**（扫到多少 .axaml.cs、配到多少标记、解析出多少签名、映射多少构造），
///    这样这条判据自己坏掉时一眼看得出坏在哪层，而不是只剩一句"0 处"。
/// </summary>
public sealed class FanOutPaintSourceRatchetTests
{
    private const int MeasuredFanOutSites = 16;
    private const int ResolvableConstructionFloor = 16;
    private const int ScannedCodeFileFloor = 90;

    private static readonly string[] ControlParamTypes =
    [
        "TextBlock", "Border", "Grid", "Panel", "StackPanel", "Image",
        "Button", "Ellipse", "Path", "Control", "IconElement", "TextElement",
    ];

    private static readonly string[] PaintProperties =
        ["Foreground", "Background", "BorderBrush", "Fill", "Stroke"];

    [Fact]
    public void FanOutPaintSites_AreCountedExactly()
    {
        var root = Path.Combine(RepoRoot(), "desktop", "LanMountainDesktop", "Views");
        var hits = new List<string>();
        var codeFiles = 0;
        var pairedMarkup = 0;
        var resolvedConstructions = 0;

        // 从**标记那一侧**走：这条文件里现成能跑通的做法（DuplicatedPaintSourceRatchetTests:88 也是这么配的）。
        // 反过来从 .axaml.cs 求同名 .axaml 时，本文件实测 93 个候选里 0 个配对成功——
        // 两种写法不对称的原因没有查到底，记下这条并改用可跑通的方向，比继续猜便宜。
        foreach (var markupPath in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (markupPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || markupPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var csPath = markupPath + ".cs";
            if (!File.Exists(csPath))
            {
                continue;
            }

            codeFiles++;

            pairedMarkup++;
            var code = File.ReadAllText(csPath);
            var markup = File.ReadAllText(markupPath);
            var named = Regex.Matches(markup, "x:Name=\"([A-Za-z_]\\w*)\"")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToHashSet();
            if (named.Count == 0)
            {
                continue;
            }

            foreach (Match sig in Regex.Matches(code, @"\b(?:class|record)\s+(\w+)\s*\(([^{]*)\)"))
            {
                var parameters = sig.Groups[2].Value
                    .Split(',')
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .Select(p => p.Split(new[] { ' ' }, 2))
                    .Where(bits => bits.Length == 2 && ControlParamTypes.Contains(bits[0]))
                    .Select(bits => bits[1].Trim())
                    .ToList();
                if (parameters.Count == 0)
                {
                    continue;
                }

                foreach (Match creation in Regex.Matches(
                    code, @"new\s+" + Regex.Escape(sig.Groups[1].Value) + @"\s*\(([^;()]*)\)"))
                {
                    var args = creation.Groups[1].Value
                        .Split(',')
                        .Select(a => a.Trim())
                        .Where(a => a.Length > 0)
                        .ToList();
                    if (args.Count == 0)
                    {
                        continue;
                    }

                    var mappedHere = false;
                    for (var index = 0; index < args.Count && index < parameters.Count; index++)
                    {
                        if (!named.Contains(args[index]))
                        {
                            continue;
                        }

                        mappedHere = true;
                        var field = parameters[index];
                        var xName = args[index];

                        foreach (var property in PaintProperties
                                     .SelectMany(prop => Regex.Matches(
                                         code, @"\b\w+\." + Regex.Escape(field) + @"\." + prop + @"\s*=")
                                         .Cast<Match>()
                                         .Select(_ => prop))
                                     .Distinct())
                        {
                            var tagMatch = Regex.Match(
                                markup,
                                "<[A-Za-z][\\w.]*[^>]*x:Name=\"" + Regex.Escape(xName) + "\"[^>]*>",
                                RegexOptions.Singleline);
                            if (tagMatch.Success
                                && Regex.IsMatch(tagMatch.Value, property + @"=""#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})"""))
                            {
                                hits.Add($"{Path.GetFileNameWithoutExtension(csPath)}:{field}→{xName}.{property}");
                            }
                        }
                    }

                    if (mappedHere)
                    {
                        resolvedConstructions++;
                    }
                }
            }
        }

        var diagnostics =
            $"诊断：配对到的 (axaml, axaml.cs) {codeFiles} 对、配到标记 {pairedMarkup} 个、可解析映射的构造站点 {resolvedConstructions} 个、命中 {hits.Count} 处";

        Assert.True(
            codeFiles >= ScannedCodeFileFloor,
            $"只扫到 {codeFiles} 个 code-behind（下限 {ScannedCodeFileFloor}）——{diagnostics}。glob 或目录一变，这条判据就看不见全貌");
        Assert.True(
            pairedMarkup >= ScannedCodeFileFloor,
            $"只有 {pairedMarkup} 个 code-behind 配到了同名 .axaml（下限 {ScannedCodeFileFloor}）——{diagnostics}。" +
            "配对方式坏了会让整条判据空转，那种绿不算证据");
        Assert.True(
            resolvedConstructions >= ResolvableConstructionFloor,
            $"只解析出 {resolvedConstructions} 个「把标记具名控件传进视觉对象」的构造站点（下限 {ResolvableConstructionFloor}）——{diagnostics}。" +
            "形参类型名单少列一项、正则退化或目录改名都会让它安静地少认一族；那种 0 不是\"已经收干净\"");
        Assert.True(
            hits.Count == MeasuredFanOutSites,
            $"扇出型双真源实测 {hits.Count} 处，等值判据记的是 {MeasuredFanOutSites}。{diagnostics}。" +
            "降了＝真收口一族（两个数一起改小并留理由）；涨了＝新代码又把颜色写进标记、再由字段重画。" +
            "明细：" + string.Join(" | ", hits.Order(StringComparer.Ordinal)));
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LanMountainDesktop.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
