using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 同一个控件的同一个画笔属性，标记里写死一份、code-behind 又赋一份 = 一个视觉事实两份真源。
///
/// 2026-09-29 现量到的形状：把 <c>.axaml</c> 里写死的颜色属性按"元素名 + 属性名"回到配对的
/// <c>.axaml.cs</c> 里查，命中 **122 处**。其中 35 处的那一行代码早已改走
/// <c>ComponentRoleBrushes.*</c>（问主题层要角色画笔），而标记里还留着换壁纸前那个具体色值——
/// 于是"组件的文字是什么颜色"这句话在同一文件里有两种说法，而且**其中一种永远不会生效**
/// （加载后第一次主题刷新就把标记值覆盖掉了）。这一族 35 处当场改成
/// <c>{DynamicResource Adaptive*Brush}</c>：键与代码用的那个角色一致，设计期与运行期第一次说得上话。
///
/// 剩下 87 处登记在 #G1-AF（这条轴的账本）里，不在这笔范围：它们两侧都还是字面色值
/// （<c>TimerWidget</c>、<c>AnalogClockWidget</c>、<c>WhiteboardWidget</c>、学习面板那几件……），
/// 要动它们得先给那批组件建角色映射，属"烧色值"本身而不是判据缺口。
///
/// 判据口径：只数**值是一个完整十六进制字面量**的属性（改成 <c>{DynamicResource …}</c> 就不算，
/// 这正是本笔的出口）；元素要有 <c>x:Name</c> 才能配对（无名字的装饰元素没有代码引用点，不算两份真源）；
/// 代码里赋的是**同名元素的同属性**。
/// 上限只许降不许升——新写一处"标记一份 + 代码一份"就红，因为这类漂移从不报错：
/// 症状是"改了 token 那个组件颜色不动"或"设计器里看着对、跑起来是另一个色"。
/// </summary>
public sealed class DuplicatedPaintSourceRatchetTests
{
    // 2026-09-29 实测基线 122 处；本笔把其中 35 处（代码那一侧已经走 ComponentRoleBrushes 的那些）
    // 改成 {DynamicResource Adaptive…Brush}，标记与代码从此说同一个角色键 → **87**。
    private const int DuplicatedPaintCeiling = 87;

    // 总数对账：与上限分开钉，是因为这条**先于上限红**——
    // 目录改名或配对方式变了会让判据安静地少看一片（那时上限仍然"够绿"），而真收口一族是另一回事。
    // 剩下 87 处两侧都还是字面色值（Timer / AnalogClock / Whiteboard / 学习面板那几件），
    // 要动它们得先给那批组件建角色映射，属 #G1-AF 的账。
    private const int MeasuredDuplicatedTotal = 87;

    // 2026-09-29 实测：Views 下 93 个 .axaml，全部有配对的 .axaml.cs。
    private const int ScannedPairFloor = 88;

    [Fact]
    public void PaintedProperties_DoNotHaveTwoSourcesInOneControl()
    {
        var root = Path.Combine(RepoRoot(), "desktop", "LanMountainDesktop", "Views");
        var hits = new List<string>();
        var axamlFiles = 0;
        var pairs = 0;

        foreach (var markupPath in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(markupPath))
            {
                continue;
            }

            axamlFiles++;
            var codePath = markupPath + ".cs";
            if (!File.Exists(codePath))
            {
                continue;
            }

            pairs++;
            var label = Path.GetRelativePath(RepoRoot(), markupPath);
            foreach (var site in DuplicatedPaint.Find(
                File.ReadAllText(markupPath),
                File.ReadAllText(codePath)))
            {
                hits.Add($"{label} {site}");
            }
        }

        Assert.True(
            pairs >= ScannedPairFloor,
            $"只配到 {pairs} 对 (axaml, axaml.cs)，下限 {ScannedPairFloor}（Views 下 .axaml 共 {axamlFiles} 个）——" +
            "配对方式变了或目录改名会让这条判据少看一片，那种绿不算证据");
        Assert.True(
            hits.Count == MeasuredDuplicatedTotal,
            $"实测命中 {hits.Count} 处，账上记的是 {MeasuredDuplicatedTotal} 处。降了说明真收口了一族——" +
            "把对账值与上限一起改小并留下理由；升了或形状变了，说明配对/目录动了，这条判据现在看不见全貌");
        Assert.True(
            hits.Count <= DuplicatedPaintCeiling,
            $"同一元素同一画笔属性在标记与代码里各写一份，实测 {hits.Count} 处，上限 {DuplicatedPaintCeiling}："
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", hits)
            + Environment.NewLine + "标记里那个字面值在第一次主题刷新后就不再生效——"
            + "要么让它走 {DynamicResource Adaptive…Brush}（与代码同一个角色键），要么别再在代码里赋第二次。");
    }

    /// <summary>
    /// 判据自己的注入点：认不出"两份真源"的那把尺子等于没有。
    /// 正例一格（同元素同属性双向赋值），四格反例：只有标记、只有代码、
    /// 标记已经走 DynamicResource（本笔的出口形态，不许再被算进来）、赋的是另一个属性。
    /// </summary>
    [Fact]
    public void Scanner_RecognizesDuplicatedPaintAndIgnoresTheRest()
    {
        var markup = """
            <Border x:Name="CardBorder" Background="#FCFCFD" CornerRadius="18">
                <TextBlock x:Name="TitleTextBlock" Foreground="#202327" />
            </Border>
            """;

        Assert.Equal(
            new[] { "CardBorder.Background", "TitleTextBlock.Foreground" },
            DuplicatedPaint.Find(markup, "CardBorder.Background = Raised(this); TitleTextBlock.Foreground = Primary(this);"));

        // 只有标记值：组件根本没重画它，属 #43 的账，不属这条判据。
        Assert.Empty(DuplicatedPaint.Find(markup, "_ = 1;"));

        // 只有代码赋值。
        Assert.Empty(DuplicatedPaint.Find(
            """<Border x:Name="CardBorder" CornerRadius="18" />""",
            "CardBorder.Background = Raised(this);"));

        // 出口形态：标记走 DynamicResource 后，即便代码也赋一次，也不该再算"字面量两份真源"。
        Assert.Empty(DuplicatedPaint.Find(
            """<Border x:Name="CardBorder" Background="{DynamicResource AdaptiveSurfaceRaisedBrush}" />""",
            "CardBorder.Background = Raised(this);"));

        // 属性不同不算（Background 与 BorderBrush 是两件事）。
        Assert.Empty(DuplicatedPaint.Find(
            """<Border x:Name="CardBorder" Background="#FCFCFD" />""",
            "CardBorder.BorderBrush = Outline(this);"));

        // 没有 x:Name 的元素没有代码引用点，不是两份真源。
        Assert.Empty(DuplicatedPaint.Find(
            """<Border Background="#FCFCFD"><TextBlock Foreground="#202327" /></Border>""",
            "SomeOther.Field.Background = X;"));
    }

    private static bool IsBuildArtifact(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

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

        throw new InvalidOperationException("找不到仓库根（用 LanMountainDesktop.slnx 定位）");
    }
}

/// <summary>
/// "标记写死 + 代码再赋一次"的扫描器。仓库格与注入格共用这一份实现——
/// 注入格是它的正/反例校具，不构成第二条独立判据。
/// </summary>
internal static class DuplicatedPaint
{
    private static readonly Regex OpenTag = new(
        "<([A-Za-z][A-Za-z0-9]*)\\s([^<>]*?)>", RegexOptions.Compiled);

    private static readonly Regex ElementName = new(
        "x:Name=\"([A-Za-z_]\\w*)\"", RegexOptions.Compiled);

    private static readonly Regex ColorAttribute = new(
        "(\\w+)=\"#[0-9A-Fa-f]{6,8}\"", RegexOptions.Compiled);

    internal static IEnumerable<string> Find(string markup, string code)
    {
        var spans = new List<(int Start, int End, string Name)>();
        foreach (Match tag in OpenTag.Matches(markup))
        {
            var name = ElementName.Match(tag.Groups[2].Value);
            spans.Add((tag.Index, tag.Index + tag.Length, name.Success ? name.Groups[1].Value : string.Empty));
        }

        var found = new List<string>();
        foreach (Match attribute in ColorAttribute.Matches(markup))
        {
            string owner = null;
            foreach (var span in spans)
            {
                if (attribute.Index >= span.Start && attribute.Index < span.End)
                {
                    owner = span.Name;
                    break;
                }
            }

            if (string.IsNullOrEmpty(owner))
            {
                continue;
            }

            var property = attribute.Groups[1].Value;
            if (Regex.IsMatch(code, "\\b" + owner + "\\." + property + "\\s*="))
            {
                found.Add(owner + "." + property);
            }
        }

        return found;
    }
}
