using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 裸色值字面量只许住在主题层（#G1-AF）。
///
/// 2026-09-26 重量：此前账上写"6 个色值共约 100 处"，那只数了最常出现的 6 个值，而且用的正则
/// 只认 8 位写法（#AARRGGBB），把 <c>"#E8EAED"</c> 这种 6 位整族漏了。
/// 按 <c>desktop/LanMountainDesktop/Views/**/*.cs</c> 里所有引号内的 #RRGGBB / #AARRGGBB 数，
/// 实测 <b>1236 处</b>；扣掉下面两张内容色板（546 + 13）之后，组件里手写颜色的还剩
/// <b>677 处 / 41 个文件</b>（这句 2026-09-29 更正过：原文写的是"31 个文件"，那是把组件目录与整个 Views
/// 两份清单数混了——同一个总数分布在 41 个文件里）。同一口径下主题层自己（Theme/ 与 Services 里的 ThemeColorSystemService /
/// MaterialSurfaceService / GlassEffectService）有 61 处——那些是调色板的算法本身，是**该**写字面量的地方。
///
/// 为什么组件里不该有：主题层的值是随壁纸算出来的（Monet 种子 + WCAG 对比度兜底，
/// ThemeColorSystemService.cs:117-124 把 textSecondary/textMuted 对实际 surfaceRaised 做 EnsureContrast），
/// 而组件里手写 <c>isNight ? "#FFFFFFFF" : "…"</c> 这种三元组等于绕过整套对比度保证——
/// 换一张浅色壁纸时"夜里是白字"的那批文字就直接看不见。这正是 G1-AF 问的"哪些该走 token"。
///
/// 这一格不替谁烧完 677 处，它先做两件能立刻生效的事：
/// ① 上限棘轮：总数只许降不许升，烧一族改小一次并留一行理由（与逐字族/零引用那几条同一套路）；
/// ② 覆盖面下限 + 总数对账：扫到的文件数或总数与记下的实测值不符就红——
/// 目录改名、glob 变窄、文件被挪走这三件事都会让判据"安静地看不见一部分"，那种绿不算证据。
/// 豁免只收实测到的两张内容色板（不是"大概也是吧"）：它们的字面量是数据而不是主题绕道，
/// 且已由 DuplicatedLiteralDictionaryRatchetTests 盯住重复。
/// </summary>
public sealed class ColorLiteralRatchetTests
{
    // 2026-09-26 实测：Views 里引号内裸色值 1236 处，减去下面两张豁免表（546 + 13）= 677。
    // 2026-09-29 烧掉第一族：状态文字那对『#8B95A5 / #6A6F77』——10 处调用点、18 个引号内字面量，
    // 改走 ComponentRoleBrushes.MutedText（它问主题层要 AdaptiveTextMutedBrush，那条链按 surfaceRaised
    // 混色后再 EnsureContrast）。677 − 18 = 659。这一笔只动这一个数，豁免侧的 559 一处未变。
    private const int ColorLiteralCeiling = 659;

    // 2026-09-26 实测：剩下这些分布在 31 个文件里。
    // ↑ 这句今天核对是错的：677 处实际分布在 **41** 个文件里（当时把"组件目录"与"整个 Views"两份清单
    // 数混了）。下限仍是 30，别把它当实测值引用——要实测值就现跑。
    private const int ScannedFileFloor = 30;

    // 2026-09-29 实测的总数对账值（组件 659 + 豁免 559），文件被改名/挪走时这条会先红。
    private const int MeasuredGrandTotal = 1218;

    // 2026-09-29 新量的一面：.axaml 里的颜色属性此前这条判据**完全看不见**（只扫 *.cs）。
    // 量出来是 412 处 / 40 个文件——比组件里剩下的 659 少不到一半，绝不是"边角料"。
    // 单独一个上限、单独一个测试，是为了红的时候直接说出是哪一面瞎了或涨了。
    private const int MarkupColorLiteralCeiling = 412;
    private const int MarkupFileFloor = 38;

    /// <summary>
    /// 内容色板：键是"天气现象 / 学科"这种业务条目，不是界面角色色。
    /// 按文件名点名豁免，理由写在行尾——再加豁免条目要付同样的解释成本。
    /// </summary>
    private static readonly HashSet<string> ExemptDataPalettes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MaterialWeatherVisualTheme.cs",   // 546 处：每种天气现象的视觉配色，是数据表
        "SubjectColorService.cs",           // 13 处：学科固定色，是数据表
    };

    private static readonly Regex HexLiteral = new(
        "\"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\"", RegexOptions.Compiled);

    private static readonly Regex MarkupHexAttribute = new(
        "=\"(?:#[0-9A-Fa-f]{8}|#[0-9A-Fa-f]{6})\"", RegexOptions.Compiled);

    [Fact]
    public void ColorLiterals_DoNotSpreadInsideViews()
    {
        var root = Path.Combine(RepoRoot(), "desktop", "LanMountainDesktop", "Views");
        var total = 0;
        var exempted = 0;
        var filesScanned = 0;
        var hottest = new List<(string File, int Count)>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hits = HexLiteral.Matches(File.ReadAllText(file)).Count;
            if (hits == 0)
            {
                continue;
            }

            if (ExemptDataPalettes.Contains(Path.GetFileName(file)))
            {
                exempted += hits;
                continue;
            }

            filesScanned++;
            total += hits;
            hottest.Add((Path.GetRelativePath(RepoRoot(), file), hits));
        }

        Assert.True(
            total + exempted == MeasuredGrandTotal,
            $"总数与记下的实测值不符：组件 {total} + 豁免 {exempted} = {total + exempted}，2026-09-26 记的是 {MeasuredGrandTotal}。" +
            "文件被改名/挪走会让这条判据安静地少看一部分；真烧了一族的话，把三个数一起改小并留理由");
        Assert.True(
            filesScanned >= ScannedFileFloor,
            $"只扫到 {filesScanned} 个带裸色值的 Views 文件（下限 {ScannedFileFloor}）——" +
            "目录/glob 变了或测试没跑到全部组件，这条判据现在看不见全貌");
        Assert.True(
            total <= ColorLiteralCeiling,
            $"Views 组件里手写裸色值实测 {total} 处，上限 {ColorLiteralCeiling}。涨了说明又在组件里绕过主题层：" +
            "那会跳过对比度兜底，换浅色壁纸时字会看不见。" +
            $"最集中的几处：{string.Join(", ", hottest.FindAll(h => h.Count >= 25).ConvertAll(h => $"{h.File}={h.Count}"))}");
    }

    /// <summary>
    /// 第二面：<c>.axaml</c> 里写死在属性上的颜色。这一面以前**根本没被这条判据看过**（它只扫 *.cs），
    /// 于是"组件里没有裸色值了"这句话可以同时意味着"标记里还攒着 412 处"。
    /// 判据口径：属性值整体是一个 #RGB/#AARRGGBB 串就算一处（按属性名白名单会漏掉 <c>Fill</c>/
    /// <c>Stroke</c>/<c>CaretBrush</c> 这些，宁可宽一点——这条数的是"字面量在不在标记里"，不是"这是哪个属性"）。
    /// </summary>
    [Fact]
    public void MarkupColorLiterals_DoNotSpreadEither()
    {
        var root = Path.Combine(RepoRoot(), "desktop", "LanMountainDesktop", "Views");
        var total = 0;
        var files = 0;
        var hottest = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hits = MarkupHexAttribute.Matches(File.ReadAllText(file)).Count;
            if (hits == 0)
            {
                continue;
            }

            files++;
            total += hits;
            if (hits >= 20)
            {
                hottest.Add($"{Path.GetRelativePath(RepoRoot(), file)}={hits}");
            }
        }

        Assert.True(
            files >= MarkupFileFloor,
            $"只扫到 {files} 个带颜色属性的 .axaml（下限 {MarkupFileFloor}，2026-09-29 实测 40）——" +
            "glob 变窄或目录改名会让这一面安静地瞎掉，那种绿不算证据");
        Assert.True(
            total <= MarkupColorLiteralCeiling,
            $"Views 的标记里手写颜色属性实测 {total} 处，上限 {MarkupColorLiteralCeiling}。" +
            $"标记里写死的颜色同样绕过主题层的对比度兜底。最集中的几处：{string.Join(", ", hottest)}");
    }

    /// <summary>
    /// 判据自己的注入点：数不到裸色值的那把尺子等于没有。
    /// 这一格不读仓库，喂一段已知文本，验正则认得 6 位与 8 位两种写法、
    /// 同时不认 <c>#RGB</c> 短写与 9 位串（Views 里实测没有这两种，认了就会误报）。
    /// </summary>
    [Fact]
    public void Scanner_RecognizesBothLiteralShapes()
    {
        const string sample = """
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFFFF"));
            TimerTextBlock.Foreground = ComponentPaint.CreateBrush("#E8EAED");
            Border.Background = new SolidColorBrush(Color.Parse("#33FFFFFF"));
            var notALiteral = "#RGB";
            var tooLong = "#FFFFFFFFF";
            """;

        Assert.Equal(3, HexLiteral.Matches(sample).Count);
    }

    /// <summary>
    /// 标记那一面的注入点：验它认属性上的 6/8 位串、不认没有引号的写法与 7 位串。
    /// 少了这一格，"标记面 0 处"随时可能只是那个正则不再匹配（同一个坑在字面量表那条轴上踩过：
    /// 把 IReadOnlyDictionary 拼成 IReadonly 时报过"全仓 0 张表"的假零）。
    /// </summary>
    [Fact]
    public void MarkupScanner_RecognizesQuotedAttributeLiterals()
    {
        const string markup = """
            <TextBlock Foreground="#8B95A5" Background="#33FFFFFF" />
            <Border CornerRadius="12" />
            <SymbolIcon Fill=#8B95A5 />
            <TextBlock Text="not a color"/>
            <TextBlock Stroke="#1234567"/>
            """;

        Assert.Equal(2, MarkupHexAttribute.Matches(markup).Count);
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
