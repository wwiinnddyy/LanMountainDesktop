using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 重复真源的"只许降不许升"棘轮：把两把普查尺子的结果冻成上限。
///
/// 为什么值得冻住：<c>dump-dup-methods.py</c> 与 <c>dump-drift-methods.py</c> 是普查工具——它们排队待办，
/// 但没有任何东西阻止新增一份复制。本仓已经吃过这个亏：圆角算式、亮度算式、JSON 路径读法、
/// 语言码归一化……每一族都是"先有人抄，事后才发现抄了 N 份"，而且抄的时候不报错。
/// 冻住之后，新抄一份就是红灯，收口一份就必须把上限改小（改小是自愿的，改大必须写理由）。
///
/// 两个口径与脚本逐字对齐（2026-09-22 交叉验证过：C# 与 python 在同一棵树上得出同一组数）：
/// ① <b>逐字相同</b>的方法体族（<c>dump-dup-methods.py</c>）：要求签名一行、<c>{</c> 单独一行
///    （本仓是 Allman 风格，按 K&R 写的解析器会一条都不匹配、报出一个假的 0），
///    且方法体去掉空行与 <c>//</c> 注释后**至少 2 行**——单行的转发不算"复制了一份逻辑"。
/// ② <b>同名不同体</b>的漂移族（<c>dump-drift-methods.py</c>）：同一个方法名有 ≥2 种归一化后的体。
///    归一化会折叠空白、把字符串字面量换成占位符，所以"只差一句文案"的不算漂移。
///    这一族专门抓"家被绕开"：2026-09-22 就是它挖出 13 个组件各抄一份
///    <c>ResolveUnifiedMainRadiusValue</c>（家早就存在）。
/// </summary>
public sealed class DuplicateImplementationRatchetTests
{
    /// <summary>
    /// 实测：64 组逐字相同、且**至少两条语句**的方法体。只能降，要升必须在这里写清理由。
    /// 66 → 64 是真收口：6 个学习组件各抄一遍的"活跃页上下文"三步
    /// （<c>_isOnActivePage</c> 落位 → <c>UpdateMonitoringLeaseState()</c> → 只在"从不在到在"时补一次刷新）
    /// 收进 <c>StudyComponentLifecycle.ApplyPageContext</c>；抄本之间只差最后那一下刷新
    /// （4 个 <c>RefreshVisual()</c>、2 个 <c>_renderGate.Queue(…)</c>），
    /// 所以这一笔把两族（4 份同文 + 2 份同文）一起消掉，并补上第一条行为钉
    /// <c>StudyComponentLifecycleTests</c>（钉的就是那个"只首次刷新"的守卫）。
    /// 之前几笔：68 → 66 收 11 个组件各写一遍的 detach 三连（进 <c>ComponentRefreshLifetime.Detach</c>）；
    /// 71 → 68（三族）收 <c>NormalizeAutoRefreshIntervalMinutes</c>（6 处各抄 11 行，
    /// 绕开的正是 <c>RefreshIntervalCatalog.Normalize</c>，净 -108 行，并给家补第一条行为钉）；
    /// 73 → 71 收两族（6 处"判黑夜 + 重排"进 <c>ComponentThemeMode.RefreshNightVisual</c>、
    /// 6 处学习组件 detach 四步进 <c>StudyComponentLifecycle.Detach</c>）；
    /// 74 → 73 收 7 个学习组件各抄一份的
    /// "读快照 → 归一语言 → 取学习监测开关"（进 <c>StudyComponentSettings.Reload</c>）；
    /// 从 86 降到 74 有两笔，别记成一笔：
    /// ① 判据修正（按语句数而不是按行数）去掉 12 族单语句转手——它们本来就不是"复制了一份逻辑"；
    /// ② 18 个组件的 ApplyCellSize 收进 <c>ComponentDesignMetrics.ApplyCellSize</c>。
    ///    这一笔对族数<b>没有净影响</b>：收口前那 13 个组件是一族逐字相同的两行体，
    ///    收口后如果还按行数算，18 处转手调用又是新的一族同文——行数骗人的地方就在这。
    ///    它真正的收益是"钳到最少 1 格再重排"这段逻辑从各写一遍变成只有一处（18 个调用点）。
    /// 64 → 63 收一族（5 个组件各抄的"只在黑夜档真的翻了才重画"进
    /// <c>ComponentThemeMode.RefreshNightVisualIfChanged</c>：原本 3 处逐字 7 行 + 2 处带 <c>force</c> 的漂移体，
    /// 收完 5 个调用点都是单语句转手，按本判据口径不再算"复制了一份逻辑"）。
    /// 63 → 62 收一族（每日一词 1x1 与 2x2 读的是<b>同一对设置键</b>，所以那 13 行是逐字相同的两份，
    /// 进 <c>DailyWordAutoRefresh.Apply</c>；顺带把编辑器注册表里第三份 <c>360</c> 也指回这个常量）。
    /// 62 → 61 收一族（头像占位字 15 行逐字两份：<c>CurrentUserProfileService</c> 与 <c>MainWindow.DesktopPaging</c>
    /// 各一份，进 <c>Services/Monogram.cs</c>，5 个调用点改走家；改了头像规则而磁贴没跟上，
    /// 症状是同一个人/同一个磁贴两种缩写，不报错）。
    /// 61 → 59 收两族（<c>NormalizeExistingDirectory</c> 与 <c>NormalizeExistingFile</c> 各 2 份逐字 17 行：
    /// Core 的 <c>AppVersionProvider</c> 与宿主的 <c>AppRestartService</c> 各抄一对，
    /// 进 <c>Shared/IO/ExistingPath.cs</c>，10 个调用点改走家——两份算的是"这条路径能不能信"，
    /// 漂开的后果是同一个安装被启动器与宿主判成两种结论）。
    /// 59 → 58 收一族（安装期"待删除包暂存目录"的收尾在 Core 的 <c>AirAppPackageInstaller</c>
    /// 与启动器的 <c>AirAppInstallerService</c> 各抄一份逐字 14 行，宿主的 <c>AirAppRuntimeService</c>
    /// 还有第三份、而且**多一段"空目录顺手删掉"**——三份做的不是同一件事。收进
    /// <c>Shared/AirAppPendingDeletionDirectory</c> 时取超集那版：安装器现在也会回收留空的目录，
    /// 目录只在需要时被重建，没有任何一侧依赖它一直存在（这条差异本身写在家的注释里）。
    /// 家新方法刻意取名 <c>PathFor</c>/<c>CleanupAfterInstall</c>：先用了 <c>Resolve</c>/<c>Cleanup</c>，
    /// 漂移普查当场从 192 涨到 194——不是多了重复，是**通用名把无关实现并进同一族**，上限红得对。）
    /// </summary>
    private const int IdenticalBodyFamilyCeiling = 58;

    /// <summary>
    /// 今天实测：192 个方法名存在 ≥2 种体。只能降，要升必须在这里写清理由。
    /// 191 → 192 是**改判据**（又一处"判据瞎了"，与下面 193 → 192 同一类）：
    /// <c>void Foo() { }</c> 这种"有实现、但什么都不做"以前与接口方法的声明一起被当成"没有体"跳过，
    /// 于是全仓 28 处空实现全体隐身；现在空实现算一种体（规范成空串），有一族因此显形。
    /// 这笔账要求同时钉住"空实现"本身：见下面 <c>emptySites</c> 的指名锚点。
    /// 192 → 191 是真收口（与上面 71 → 68 同一笔）：<c>NormalizeAutoRefreshIntervalMinutes</c>
    /// 的 6 处各抄本改调早就存在的 <c>RefreshIntervalCatalog.Normalize</c>，这个名字连同 3 种体一起消失。
    /// 再往前 193 → 192 是**改判据**，不是收口（一处代码都没删）：
    /// <c>NormalizeBody</c> 过去只认"签名行尾的 <c>=></c>"，两种真实写法被它整条丢掉——
    /// ① Allman 箭头体（<c>private void UpdateLanguageCode()</c> 换行 <c>=></c> 表达式）；
    /// ② 插值字符串里的 <c>{message}</c> 被当成方法体的开括号，一路扫到类末尾
    ///    （实测 AirAppRuntimeLogger.cs:7 的 <c>Info</c>、PlondsApplyPaths.cs:39 的 <c>GetSnapshotPath</c>）。
    /// 修完后与 <c>dump-drift-methods.py</c> **逐处对齐**：两边同为 192 族、5449 处声明（收口前），
    /// 站点清单（文件+行号）完全相同——这条口径差异（以前是 196 对 193、差 3 族没对过）就此结清。
    /// 组成变化：<c>nameof</c>／<c>VALUES</c>／<c>SelectionOption</c>／<c>UpdateMonitoringLeaseState</c> 掉出
    /// （它们的"多种体"是吞出来的假体）；<c>GetSessionsAsync</c>／<c>GetThemeBrush</c>／<c>SetValue</c> 新进来
    /// （接口默认实现真的有多种写法，这是以前被假体挤掉的漏报）。
    /// 被这条修反转量的是最大的两族：<c>L</c> 50 处但只有 7 种体（旧数 43 处 / 18 种体），
    /// <c>UpdateLanguageCode</c> 11 处 / 3 种体（旧数 11 处 / 10 种体）——它两仍是待收口的最大两族；
    /// 但 <c>L</c> 的 50 处里 44 处是**单条语句的转手**（<c>return _localizationService.GetString(...)</c>），
    /// 按这把尺子的口径（单语句转手不算复制了一份逻辑）它不构成收口目标，别再为凑族数去动它。
    /// </summary>
    private const int DriftFamilyCeiling = 192;

    /// <summary>
    /// 漂移普查认领的声明处数下限（今天实测 5443：改判据后 5449，收掉 6 处各抄本少 6）。
    /// 钉这个不是为了查新增，是为了查**判据自己塌掉**：
    /// 上面那两处 bug 都是"少认声明"，族数看着像收口（193→189），实际是普查瞎了。
    /// 只冻族数会被这种错法骗过去，冻住认领量就不会。
    /// </summary>
    private const int DriftCensusSiteFloor = 5400;

    private static readonly string[] ScanDirectories =
        ["core", "desktop", "airapp", "install", "platform", "packaging", "mobile"];

    private static readonly string[] SkipPathParts = ["obj", "bin", "artifacts", "node_modules"];

    private static readonly Regex AllmanSignature = new(
        @"^\s*(private|internal|public)\s+(static\s+)?(async\s+)?[\w<>?\[\],\. ]+?\b(?<name>[A-Za-z_]\w*)\s*\([^)]*\)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex OpeningBraceOnly = new(@"^\s*\{\s*$", RegexOptions.Compiled);

    private static readonly Regex DriftSignature = new(
        @"^\s*(?:public|private|protected|internal)?\s*" +
        @"(?:static\s+|sealed\s+|override\s+|virtual\s+|async\s+|partial\s+|new\s+)*" +
        @"(?:[A-Za-z_][\w<>\[\]?,\. ]*?\s+)?(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\([^)]*\)\s*(?:=>.*)?\{?\s*\}?\s*$",
        RegexOptions.Compiled);

    private static readonly string[] NonMethodNames =
    [
        "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return",
        "get", "set", "add", "remove", "init", "when", "where", "select", "from",
    ];

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex StringLiteral = new(@"""[^""]*""", RegexOptions.Compiled);

    [Fact]
    public void IdenticalMethodBodyFamilies_DoNotGrow()
    {
        var repoRoot = RepoRoot();
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var path in EnumerateSources(repoRoot))
        {
            var lines = File.ReadAllLines(path);
            var relative = Relative(repoRoot, path);
            // index 在吃完一个方法体后会跳到 } 之后：与脚本一致，方法体内部的局部函数不算"第二个方法"。
            for (var index = 0; index < lines.Length - 2;)
            {
                var signature = AllmanSignature.Match(lines[index]);
                if (!signature.Success || !OpeningBraceOnly.IsMatch(lines[index + 1]))
                {
                    index++;
                    continue;
                }

                var end = FindBodyEnd(lines, index + 1);
                if (end < 0)
                {
                    index++;
                    continue;
                }

                var body = lines
                    .Skip(index + 2)
                    .Take(end - index - 2)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
                    .ToList();
                // 判的是"复制了一份逻辑"，不是"占了两行"：按深度 0 的分号数语句数，
                // 单条语句（哪怕写成两行）就是转手/委托，不是重复真源。
                // 实测样本：18 个组件的 ApplyCellSize 收口成
                //     ComponentDesignMetrics.ApplyCellSize(
                //         ref _currentCellSize, cellSize, UpdateAdaptiveLayout);
                // 之后仍是一族"逐字相同的两行体"——按行数它会一直算成重复，那是指标在骗人。
                if (CountStatements(body) < 2)
                {
                    index = end + 1;
                    continue;
                }

                var key = $"{signature.Groups["name"].Value}|{body.Count}|{string.Join('\n', body)}";
                if (!groups.TryGetValue(key, out var sites))
                {
                    sites = [];
                    groups[key] = sites;
                }

                sites.Add($"{relative}:{index + 1}");
                index = end + 1;
            }
        }

        var families = groups.Where(pair => pair.Value.Count >= 2).ToList();
        AssertEqual(
            IdenticalBodyFamilyCeiling,
            families.Count,
            "逐字相同的方法体族数变了：变多说明有人又抄了一份（先收口再改上限，或在上面写理由并挂待办）；" +
            "变少是好事，把常量改成新的数就是收口的记账");
    }

    [Fact]
    public void SameNameDifferentBodyFamilies_DoNotGrow()
    {
        var repoRoot = RepoRoot();
        var bodiesByName = new Dictionary<string, NameTally>();
        var emptySites = new List<string>();

        foreach (var path in EnumerateSources(repoRoot))
        {
            var lines = File.ReadAllLines(path);
            var relative = Relative(repoRoot, path);
            for (var index = 0; index < lines.Length; index++)
            {
                var raw = lines[index];
                var trimmed = raw.Trim();
                if (trimmed.Length == 0 ||
                    trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("/*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                var match = DriftSignature.Match(raw);
                if (!match.Success)
                {
                    continue;
                }

                var name = match.Groups["name"].Value;
                if (NonMethodNames.Contains(name, StringComparer.Ordinal))
                {
                    continue;
                }

                var normalized = NormalizeBody(lines, index);
                if (normalized is null)
                {
                    continue;
                }

                if (!bodiesByName.TryGetValue(name, out var tally))
                {
                    tally = new NameTally();
                    bodiesByName[name] = tally;
                }

                if (normalized.Length == 0)
                {
                    emptySites.Add($"{Path.GetFileName(relative)}|{name}");
                }

                tally.Record(relative, normalized);
            }
        }

        // 空实现不能钉"数量下限"——修掉一个桩就会假红；钉**指名锚点**才是"这条区分还活着"的证据。
        // `void Foo() { }` 曾经被当成"没有这个方法"整条跳过，于是 28 处空实现全体隐身。
        Assert.Contains("RssReaderWidget.axaml.cs|ApplyCellSize", emptySites);
        Assert.True(
            emptySites.Count >= 1,
            $"一处空实现都没数到（当前 {emptySites.Count} 处）：空实现与无实现声明的区分又被合并回去了");

        // 与脚本同门槛：≥3 处声明、≥2 种体、且横跨 ≥3 个文件，才算"同名不同体"要收口的族。
        // 认领量下限先查：判据瞎了会让族数"假收口"，那时候比族数没意义。
        var censusSites = bodiesByName.Values.Sum(tally => tally.Sites);
        Assert.True(
            censusSites >= DriftCensusSiteFloor,
            $"漂移普查只认领到 {censusSites} 处声明，低于下限 {DriftCensusSiteFloor}（今天实测 5456）。" +
            "族数没变也说明判据在丢声明：查 NormalizeBody 又漏掉了哪种成员写法（历史上漏过 Allman 箭头体与插值字符串的大括号）");

        var driftFamilies = bodiesByName.Count(pair => pair.Value.VariantCount >= 2 &&
                                                      pair.Value.Sites >= 3 &&
                                                      pair.Value.Files >= 3);
        AssertEqual(
            DriftFamilyCeiling,
            driftFamilies,
            "同名不同体的漂移族数变了：变多说明同一个名字又多了一种实现（这正是「家被绕开」的形态，" +
            "先收口或写理由改上限）；变少是好事，把常量改成新的数就是收口的记账");
    }

    /// <summary>
    /// 与脚本一致：折叠空白、字符串字面量换成占位符，所以"只差一句文案"不算第二种体。
    /// 三种成员形态分开处理，因为本仓**同时**用它们（Allman 大括号、行尾 <c>=></c>、另起一行的 <c>=></c>）：
    /// 只认其中一种就会把别的形态的声明整条丢掉或整段吞掉——2026-09-22 实测这样丢了 53 处声明、5 个整文件。
    /// </summary>
    private static string? NormalizeBody(string[] lines, int signatureIndex)
    {
        var signatureLine = lines[signatureIndex].TrimEnd();
        var trimmed = signatureLine.Trim();
        var ahead = NextNonEmptyLine(lines, signatureIndex + 1);
        var arrow = signatureLine.IndexOf("=>", StringComparison.Ordinal);
        var allmanBrace = !signatureLine.Contains('{') && ahead.StartsWith('{');
        // 表达式体优先，且**先于**"本行有没有大括号"的判断：插值字符串里的 `{message}` 也是大括号，
        // 把它当方法体的开括号会一路扫到类末尾（实测 AirAppRuntimeLogger.cs:7 的 `Info`）。
        // 认"这一行以 `;` 或 `=>` 收尾 + 括号配平"，才不会把 K&R 写的 `{ …() => …; }` 误当成表达式体。
        if (arrow >= 0 && !allmanBrace &&
            (trimmed.EndsWith(";", StringComparison.Ordinal) || trimmed.EndsWith("=>", StringComparison.Ordinal)) &&
            signatureLine.Split('{').Length == signatureLine.Split('}').Length)
        {
            return Normalise(ArrowTail(lines, signatureIndex + 1, signatureLine[(arrow + 2)..]));
        }

        if (!signatureLine.Contains('{') && !allmanBrace)
        {
            // 没有大括号、下一行也不是 `{`：要么 `=>` 另起一行（那是实现），要么是无实现的声明。
            // 两者必须分开：null＝"这不是实现"（接口方法、abstract 声明），
            // 空串＝"有实现但什么都不做"（`void Foo() { }`），后者是站点——"声明了契约却不执行"正是这条尺子要抓的。
            return ahead.StartsWith("=>", StringComparison.Ordinal)
                ? Normalise(ArrowTail(lines, signatureIndex + 2, ahead[2..]))
                : null;
        }

        var depth = 0;
        var started = false;
        var body = new List<string>();
        for (var cursor = signatureIndex; cursor < lines.Length; cursor++)
        {
            var text = lines[cursor];
            depth += text.Split('{').Length - 1;
            depth -= text.Split('}').Length - 1;
            if (text.Contains('{'))
            {
                started = true;
            }

            if (cursor > signatureIndex)
            {
                body.Add(text.Trim());
            }

            if (started && depth <= 0)
            {
                break;
            }
        }

        return Normalise(string.Join(" ", body));
    }

    /// <summary>把 <c>=></c> 之后的表达式收成一条语句：吃到下一个深度 0 的 <c>;</c> 为止，绝不越过别的成员。</summary>
    private static string ArrowTail(string[] lines, int start, string head)
    {
        var parts = new List<string>();
        if (head.Trim().Length > 0)
        {
            parts.Add(head.Trim());
        }

        var depth = parts.Sum(part => part.Split('{').Length - part.Split('}').Length - 1);
        if (depth <= 0 && parts.Count > 0 && parts[^1].EndsWith(";", StringComparison.Ordinal))
        {
            return string.Join(" ", parts).TrimEnd(';').Trim();
        }

        for (var cursor = start; cursor < lines.Length; cursor++)
        {
            var text = lines[cursor].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (depth <= 0 && (text.StartsWith('}') || text.StartsWith("//", StringComparison.Ordinal)
                    || text.StartsWith("/*", StringComparison.Ordinal) || text.StartsWith('*')))
            {
                break;
            }

            parts.Add(text);
            depth += text.Split('{').Length - text.Split('}').Length - 1;
            if (depth <= 0 && text.EndsWith(";", StringComparison.Ordinal))
            {
                break;
            }
        }

        return string.Join(" ", parts).TrimEnd(';').Trim();
    }

    private static string NextNonEmptyLine(string[] lines, int start)
    {
        for (var cursor = start; cursor < lines.Length; cursor++)
        {
            if (lines[cursor].Trim().Length > 0)
            {
                return lines[cursor].Trim();
            }
        }

        return string.Empty;
    }

    private static string Normalise(string text) =>
        Canonical(StringLiteral.Replace(Whitespace.Replace(text, " "), "\"S\"").Trim());

    /// <summary><c>{ }</c> 与 <c>{}</c> 统一成空串：空实现只许有一个规范形状，否则同一种写法会被数成两种体。</summary>
    private static string Canonical(string text) =>
        text.Length == 0 || Whitespace.Replace(text, string.Empty) == "{}" ? string.Empty : text;

    private static int FindBodyEnd(string[] lines, int openingIndex)
    {
        var depth = 0;
        var seen = false;
        for (var cursor = openingIndex; cursor < lines.Length; cursor++)
        {
            foreach (var character in lines[cursor])
            {
                if (character == '{')
                {
                    depth++;
                    seen = true;
                }
                else if (character == '}')
                {
                    depth--;
                }
            }

            if (seen && depth == 0)
            {
                return cursor;
            }
        }

        return -1;
    }

    private static IEnumerable<string> EnumerateSources(string root)
    {
        foreach (var directory in ScanDirectories)
        {
            var full = Path.Combine(root, directory);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = Path.DirectorySeparatorChar + file + Path.DirectorySeparatorChar;
                if (SkipPathParts.Any(part => normalized.Contains(
                        Path.DirectorySeparatorChar + part + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path)
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

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

    /// <summary>数深度为 0 的分号：一条语句可能跨多行，字符串里的分号不算。</summary>
    private static int CountStatements(IEnumerable<string> body)
    {
        var statements = 0;
        foreach (var line in body)
        {
            var depth = 0;
            var inString = false;
            foreach (var character in line)
            {
                if (character == '"')
                {
                    inString = !inString;
                }
                else if (!inString && (character == '{' || character == '(' || character == '['))
                {
                    depth++;
                }
                else if (!inString && (character == '}' || character == ')' || character == ']'))
                {
                    depth--;
                }
                else if (!inString && character == ';' && depth == 0)
                {
                    statements++;
                }
            }
        }

        return statements;
    }

    private sealed class NameTally
    {
        private readonly HashSet<string> _variants = new(StringComparer.Ordinal);

        private readonly HashSet<string> _files = new(StringComparer.Ordinal);

        public int Sites { get; private set; }

        public int VariantCount => _variants.Count;

        public int Files => _files.Count;

        public void Record(string file, string normalizedBody)
        {
            Sites++;
            _variants.Add(normalizedBody);
            _files.Add(file);
        }
    }

    private static void AssertEqual(int expected, int actual, string because)
    {
        // 只许降不许升：变多就是有人又抄了一份；变少也红，但红的是"把上限改小"这笔记账动作。
        Assert.True(
            actual == expected,
            $"{because}{Environment.NewLine}当前 {actual} 处，上限 {expected} 处。" +
            (actual > expected
                ? "变多：先收口，或写明理由并挂待办后再改上限"
                : $"变少是进展，把常量改成 {actual} 就是这笔账"));
    }
}
