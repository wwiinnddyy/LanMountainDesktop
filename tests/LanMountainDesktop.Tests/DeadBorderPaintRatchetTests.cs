using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 描边画笔写在厚度为 0 的边框上 = 这行永远读不到（死描边）。
///
/// 起因是 #43 烧 <c>#33FFFFFF</c> 那 11 处时逐处去读：其中 4 处所在的那个 <c>RootBorder</c>
/// 在标记里写着 <c>BorderThickness="0"</c>，而整个 code-behind 从没改过这个值。
/// Avalonia 的 Border 只在厚度大于 0 时才描边，所以那几行赋值——连它带的三元组一起——
/// 从组件上线那天起就没画出一个像素。判据只看"裸色值"的话，这 5 处会被当成"待烧的角色色"，
/// 于是我把一个**看不见的边**接进了主题层，还顺手写下"夜档描边已统一"这种会说谎的账。
///
/// 这条守卫钉的是两件事：
/// ① 数量上限：死描边只许降不许升（2026-09-29 实测 5 处，同日清到 0）；
/// ② 覆盖面下限：配对扫到的 (axaml, axaml.cs) 文件数不许掉——目录改名或配对方式变了，
///    判据会安静地少看一半，那种绿不算证据。
///
/// 口径刻意保守，宁可漏报不误报：只认标记里**字面量** <c>BorderThickness="0"</c>；
/// 元素带 <c>Classes.x</c> 就跳过（类作用域样式可能把厚度抬起来，见 GlassModule.axaml 那几条
/// <c>Border.surface-*=1.2/1.5</c>）；code-behind 里只要给同名元素赋过 <c>BorderThickness</c> 也跳过。
/// 实测这 5 处三条豁免一个都没沾上（类名全空、没有任何代码改厚度）。
///
/// 普查每次从标记重新算，所以它不会在"变活"这个方向上过期：如果哪天厚度真被抬起来，
/// 那一处就从名单里消失、由别的判据接管，而不是继续冒充死码把真缺陷藏住。
/// </summary>
public sealed class DeadBorderPaintRatchetTests
{
    // 2026-09-29 实测 5 处（Baidu/Bilibili×2/Ifeng/Stcn24 的 thickness-0 根框与搜索框），同日全部删除。
    // 上限是 0：新增一处需要付"为什么允许往画不出来的边上写颜色"的解释成本。
    private const int DeadPaintCeiling = 0;

    // 2026-09-29 实测：桌面工程里 97 对 (axaml, axaml.cs)。
    private const int ScannedPairFloor = 90;

    [Fact]
    public void DeadBorderPaints_DoNotSpread()
    {
        var root = Path.Combine(RepoRoot(), "desktop", "LanMountainDesktop");
        var hits = new List<string>();
        var pairs = 0;

        foreach (var markupPath in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(markupPath))
            {
                continue;
            }

            var codePath = markupPath + ".cs";
            if (!File.Exists(codePath))
            {
                continue;
            }

            pairs++;
            var label = Path.GetRelativePath(RepoRoot(), codePath);
            hits.AddRange(DeadBorderPaint.Find(
                File.ReadAllText(markupPath),
                File.ReadAllText(codePath),
                label));
        }

        Assert.True(
            pairs >= ScannedPairFloor,
            $"只配到 {pairs} 对 (axaml, axaml.cs)，下限 {ScannedPairFloor}（2026-09-29 实测 97）——" +
            "配对方式或目录变了会让这条判据少看一片，那种绿不算证据");
        Assert.True(
            hits.Count <= DeadPaintCeiling,
            $"往厚度为 0 的边框上描边，实测 {hits.Count} 处，上限 {DeadPaintCeiling}：" +
            Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", hits) +
            Environment.NewLine + "那几行画不出像素——要么边框本来就该有厚度（把它交给 chrome/token 并说清），" +
            "要么这行是残留，删掉。留着它的代价是：改它的人以为自己在改外观。");
    }

    /// <summary>
    /// 判据自己的注入点：认不出死描边的那把尺子等于没有。
    /// 四格喂同一个扫描器，验它只把"字面 0 厚度 + 有画笔赋值 + 代码从不抬厚度"这一种算进去——
    /// 另三种（有厚度 / 代码后来抬厚度 / 带类名可能被样式接管）都必须不算，误报会逼人给
    /// 一个真看得见的边删掉画笔。
    /// </summary>
    [Fact]
    public void Scanner_SeparatesDeadPaintFromLiveBorder()
    {
        Assert.Single(DeadBorderPaint.Find(
            Markup(thickness: "0"), "Card.BorderBrush = new SolidColorBrush(night);", "Dead.cs"));

        Assert.Empty(DeadBorderPaint.Find(
            Markup(thickness: "1"), "Card.BorderBrush = new SolidColorBrush(night);", "Live.cs"));

        Assert.Empty(DeadBorderPaint.Find(
            Markup(thickness: "0"),
            "Card.BorderBrush = new SolidColorBrush(night);" +
            "Card.BorderThickness = new Thickness(1);", "Revived.cs"));

        Assert.Empty(DeadBorderPaint.Find(
            Markup(thickness: "0", classes: " Classes.surface-translucent-panel=\"True\""),
            "Card.BorderBrush = new SolidColorBrush(night);", "Styled.cs"));

        Assert.Empty(DeadBorderPaint.Find(
            Markup(thickness: "0", classes: " Classes=\"surface-translucent-panel\""),
            "Card.BorderBrush = new SolidColorBrush(night);", "StyledList.cs"));

        // 反向也要成立：标记里那串 "0" 若不是Thickness 属性，就不该被判死。
        Assert.Empty(DeadBorderPaint.Find(
            "<Border x:Name=\"Card\" Width=\"0\" />",
            "Card.BorderBrush = new SolidColorBrush(night);", "OtherZero.cs"));
    }

    private static string Markup(string thickness, string classes = "") =>
        $"""
         <UserControl xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
             <Border x:Name="Card"
                     BorderThickness="{thickness}"{classes}
                     Padding="0">
                 <TextBlock Text="x" />
             </Border>
         </UserControl>
         """;

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
/// 死描边扫描器：标记与代码都是喂进来的字符串，仓库里的那格与注入的那格共用这一份实现——
/// 注入格是它的正例/反例校具，不是第二条独立判据（两条互证只在判据各写一遍时才成立）。
/// </summary>
internal static class DeadBorderPaint
{
    private static readonly Regex OpenTag = new(
        "<([A-Za-z][A-Za-z0-9]*)\\s([^<>]*)>", RegexOptions.Compiled);

    private static readonly Regex ElementName = new(
        "x:Name=\"([A-Za-z_]\\w*)\"", RegexOptions.Compiled);

    private static readonly Regex ThicknessAttribute = new(
        "BorderThickness=\"([^\"]*)\"", RegexOptions.Compiled);

    internal static IEnumerable<string> Find(string markup, string code, string label)
    {
        var found = new List<string>();

        foreach (Match tag in OpenTag.Matches(markup))
        {
            var attributes = tag.Groups[2].Value;
            var name = ElementName.Match(attributes);
            if (!name.Success)
            {
                continue;
            }

            var thickness = ThicknessAttribute.Match(attributes);
            if (!thickness.Success || thickness.Groups[1].Value.Trim() != "0")
            {
                continue;
            }

            // 带类名的边框可能被类作用域样式把厚度抬起来（两种写法都算：Classes.x="True" 与 Classes="x"）。
            if (attributes.Contains("Classes.", StringComparison.Ordinal)
                || attributes.Contains("Classes=\"", StringComparison.Ordinal))
            {
                continue;
            }

            var assignment = code.IndexOf(name.Groups[1].Value + ".BorderBrush =", StringComparison.Ordinal);
            if (assignment < 0)
            {
                continue;
            }

            if (Regex.IsMatch(code, "\\b" + name.Groups[1].Value + "\\.BorderThickness\\s*="))
            {
                continue;
            }

            // 行号先算再插值：插值洞里直接写 code[:i] 的那个冒号会被当成格式说明符分隔符。
            var line = 1;
            for (var i = 0; i < assignment; i++)
            {
                if (code[i] == '\n')
                {
                    line++;
                }
            }

            found.Add($"{label}:{line} {name.Groups[1].Value}.BorderBrush"
                + "（标记里 BorderThickness=\"0\"，这行永远画不出来）");
        }

        return found;
    }
}
