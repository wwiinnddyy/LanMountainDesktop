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
    // 混色后再 EnsureContrast）。677 − 18 = 659。
    // 同日烧掉第二族：正文那族 17 处调用点、34 个字面量（夜档一律 #E8EAED，日档漂成 6 个近黑值：
    // #202327 / #2B2F35 / #11151D / #141922 / #151922 / #20232A），改走 ComponentRoleBrushes.PrimaryText。
    // 659 − 34 = 625。
    // 同日第三族：次要文字与图标字形 17 处 / 34 个字面量（夜档一律 #A8B1C2，日档漂成 11 个值：
    // #5E6671 #5A6069 #6B7078 #7A8088 #8A9099 #626870 #A4A9B2 #B2B7C0 #4A5466 #646C79 #7A7F89），
    // 改走 ComponentRoleBrushes.SecondaryText。625 − 34 = 591。
    // 那一族里剩下的唯一一处 #A8B1C2 故意没并进来：RecordingWidget.cs:166 的 FutureLine.Background
    // 是装饰线的底色（面板/线条角色），按文字角色并过去是换错档。
    // 同日第四族：卡片与根面板那层底，9 处 / 18 个字面量（夜档一律 #1B2129，日档 #FCFCFD #FCFBFA
    // #ECEFF3 #F4F5F7 四种），改走 ComponentRoleBrushes.RaisedSurface。591 − 18 = 573。
    // 选 raised 不是随手挑一层：主题那三个文字角色的对比度全按 raised 算（ThemeColorSystemService.cs:117-124），
    // 底与字用同一块基准，那道保证才真成立。SurfaceLadder_* 那条测试钉的就是"芯片比卡片亮(夜)/暗(昼)"的方向。
    // 同日第五族：控件底 9 处 / 18 个字面量（夜档一律 #2D3440，日档 6 个值，其中一个还是半透明
    // #14A0A6AF），改走 ComponentRoleBrushes.OverlaySurface。573 − 18 = 555。
    // 剩一处故意不并：NotificationBoxWidget.cs:355-358 的"未读/已读"两层，
    // 它白天是"未读更亮"、夜里是"未读更亮"，而主题的 raised/overlay 两层在白昼方向相反
    // （白天 overlay 比 raised 暗）——1:1 映射不存在，硬并会替产品决定"哪个状态更显眼"。
    // 烧完这一族，DailyWord2x2Widget.axaml.cs 一处不剩：带裸色值的文件数 41 -> 40。
    // 同日第六笔（#FFF 那族）只烧得动一处：BrowserWidget 的地址框底 → OverlaySurface。555 − 2 = 553。
    // 这一族的产出是"剩下的分箱"，不是总数：23 处 #FFFFFFFF 逐处读过——
    //   · 9 处在学习组件的 *ColorCandidates 数组里（那是给对比度挑选器喂的候选，不是角色色），
    //     这批表总共 **106 处 / 7 个文件**，属已登记的 G1-CD（采样派 vs 主题资源派）没并的剩余部分；
    //   · 7 处是 ColorMath.Blend / WithAlpha / GradientStop 的参数（往白提亮的算法常数）；
    //   · 2 处是内容底（WebView 视口、白板画布）——不是 UI 角色，跟着壁纸走会把网页/画纸染色；
    //   · 1 处是调用方兜底值（RemovableStorage 的 OnAccent 兜底），规则明说兜底归调用方；
    //   · 3 处是家与假数据（StudyPanelPalette.White、自绘图表的最新点、MainWindow 设计期预览卡参数）。
    // 全仓 553 处分箱实测：候选表 106 / 算法常数 23 / 其余 424（含真角色色，下族 #33FFFFFF 11 处）。
    // 同日第七笔：#33FFFFFF 那 11 处逐处读下来，4 处（+隔壁 1 处 #3FFFFFFF）根本画不出来——
    // 所在边框在标记里写着 BorderThickness="0"，而 code-behind 从没抬过厚度。
    // 这 5 行不是"颜色漂了"，是死赋值，删掉即 553 − 10 = 543（每行带三元组两侧共 2 个字面量）。
    // 判据与普查落在 DeadBorderPaintRatchetTests（上限 0，覆盖面 97 对），别再把它当角色色烧。
    private const int ColorLiteralCeiling = 543;

    // 2026-09-26 实测：剩下这些分布在 31 个文件里。
    // ↑ 这句今天核对是错的：677 处实际分布在 **41** 个文件里（当时把"组件目录"与"整个 Views"两份清单
    // 数混了）。下限仍是 30，别把它当实测值引用——要实测值就现跑。
    private const int ScannedFileFloor = 30;

    // 总数对账值：组件 543 + 豁免 559 = 1102（2026-09-29 死描边那 5 行删除后重跑）。
    // ↑ 这条会先于上限红，是为了让"文件被改名/挪走导致判据少看一片"和"真烧了一族"分得开。
    private const int MeasuredGrandTotal = 1102;

    // 2026-09-29 新量的一面：.axaml 里的颜色属性此前这条判据**完全看不见**（只扫 *.cs）。
    // 量出来是 412 处 / 40 个文件——比组件里剩下的 659 少不到一半，绝不是"边角料"。
    // 单独一个上限、单独一个测试，是为了红的时候直接说出是哪一面瞎了或涨了。
    // 同日第一笔（标记面）：412 − 35 = **377**。这 35 处不是"新发现的角色色"，而是
    // **同一个元素的同一个画笔属性在标记与代码里各写一份**的那一族（守卫见
    // DuplicatedPaintSourceRatchetTests，基线 122 → 87）：代码那一侧早已改走 ComponentRoleBrushes，
    // 标记里还留着换壁纸前的具体值——第一次主题刷新后它就再也不生效了。
    // 现在标记改成 {DynamicResource Adaptive…Brush}，用的是代码那个角色键的同一个键。
    // 同日第二笔（标记面）：349 = 377 − 28。这 28 处是**只在标记里写着的文字颜色**——
    // 热搜条目 8 处（百度 4 + B 站 4，都是 `#202327`）、Stcn24 帖子标题 8 处同一值、
    // 头像占位字 8 处 `#4A5466`、通知盒空状态 3 处 `#8B95A5` + 1 处眼睛图标字形
    // （那一处按"图标字形走次要文字"的既有口径给 secondary，不是 muted）。
    // 其中 24 处代码那一侧其实**也在重画**，只是重画走的是记录字段、字段名与 x:Name 不同
    // （`visual.TitleTextBlock.Foreground = PrimaryText(this)`，那几行的 x:Name 是 `HotItem1TextBlock`…），
    // 下一条判据按名字匹配看不见它们——那里记的 87 因此是**下界**，不是全量；
    // 这条盲区 2026-09-30 用独立实现复算并定性过（限定接收者不是问题、跨文件同名会撞出 223 处假阳，
    // 要补得做名字解析），细节写在 `DuplicatedPaintSourceRatchetTests` 的头注里。
    // 这一面（按字面量数、不认接收者）没有这个盲区。
    private const int MarkupColorLiteralCeiling = 349;

    // 总数对账（与 .cs 面同一个套路：这条先于上限红，用来分"真收口"与"判据少看一片"）。
    private const int MarkupMeasuredTotal = 349;

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
            total == MarkupMeasuredTotal,
            $"标记面实测 {total} 处，账上记的是 {MarkupMeasuredTotal} 处。降了说明真烧了一族——" +
            "把对账值与上限一起改小并留理由；升了说明又在标记里写死了新颜色");
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
