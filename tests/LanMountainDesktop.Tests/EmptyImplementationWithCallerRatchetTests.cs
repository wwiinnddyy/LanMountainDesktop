using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// "有入口、效果为空"这条轴：<b>方法体一个字都没有、却仍有活调用点</b>的成员。
///
/// 为什么值得钉：零使用棘轮数的是相反的一半——"没人调"。这条数的是"有人调，但那件事不发生"。
/// 它不崩、不报错，只是行为静默缺一块；2026-09-23 是因为先问了一句
/// "有没有组件在没有附着判据时把刷新表起起来"才撞出来的（那一趟还暴露出普查把空实现
/// 当成"没有这个方法"整条跳过）。
///
/// 口径有两种：<b>整个方法体只剩空白或一句注释</b>（同行 <c>{ }</c>、Allman 的签名/<c>{</c>/<c>}</c> 三行、
/// 以及 <c>{ // … }</c>），以及<b>体内每条语句都只是 `_ = 参数`</b>（那种缝看着像在做事，其实把传进来的东西丢了）。
/// 后者<b>只数不带括号的丢弃</b>：<c>_ = Foo()</c> 里那件事真的发生（发后即忘的异步、忽略返回值），
/// 全仓实测有 4 处那样的正当写法，混进来这条判据就没人信了。
/// 取体按<b>大括号深度</b>收尾，且数括号时先剥字符串内文本（复用 <see cref="SourceTextScanning"/>）——
/// 上一版把两件事混在一条拼平的字符串上做，"体首行是注释"的正常方法被判成空体，一次误报 40+ 处。
/// 且能找到一处不是声明的调用（成员访问 <c>x.Foo()</c>、裸调用、<c>new Foo()</c> 都算；
/// <c>void Foo(</c>、<c>public Foo(</c> 这类"名字前面隔着类型或修饰符"的是声明）。
/// 构造器也在范围内——它同样能"收一个参数然后丢掉"（名单里就有一条是这么被抓出来的）。
///
/// 名单是 <c>键 → 为什么空着是对的</c>：说不出理由的不许加，只能挂待办编号（G1-xx）。
/// 一次性的"看起来无害"不是理由：<c>Null-object</c> 要写出那个类叫什么，
/// <c>平台没有这个能力</c> 要写出是哪个平台分支把它换成了空实现。
/// </summary>
public sealed class EmptyImplementationWithCallerRatchetTests
{
    private static readonly string[] ProductionDirectories =
        ["core", "desktop", "airapp", "install", "platform", "mobile"];

    /// <summary>语料覆盖面下限：目录改名或整块移出编译时，先在这里红，而不是让名单"自己变干净"。</summary>
    private const int ScannedFileFloor = 700;

    /// <summary>
    /// 检测器金丝雀：两条都是"必须在名单里"的已知空实现，各走一条判空路径——
    /// <see cref="ActionObserver"/> 是同行 <c>{ }</c>，<see cref="IWindowPassthroughServices"/> 里的空壳是 Allman 三行。
    /// 检测器瞎了时这里红，而不是"零处未解释"的绿色假象。
    /// </summary>
    private static readonly string[] MustBeDetected =
    [
        "ActionObserver.cs|OnCompleted",
        "IWindowPassthroughServices.cs|SendToBottom",
    ];

    private static readonly Dictionary<string, string> ExplainedNoops = new(StringComparer.Ordinal)
    {
        // —— Null-object：整个类的存在意义就是"这台机器上没有这件事"，调用方按"可能没效果"写 ——
        ["SettingsWindow.axaml.cs|Rebuild"] =
            "SettingsWindow.EmptySettingsPageRegistry：没有外部设置页注册时的空注册表，Rebuild 无事可重建。",
        ["IAudioRecorderService.cs|Discard"] =
            "NoOpAudioRecorderService（麦克风不可用时工厂给的就是它）：没开过流，Discard 没有东西可丢。",
        ["IAudioRecorderService.cs|Dispose"] =
            "同上：这个替身不持有任何句柄。",
        // —— 体里只剩注释：`{ }` 的多行版，上一版判据看不见这一类 ——
        ["AirAppBase.cs|Initialize"] =
            "AirAppSdk 的虚基类默认实现，XML 注释就写着 Default implementation: do nothing / " +
            "派生类覆写它去注册自己的组件与服务；宿主照例调用它（AirAppLoader.cs:167）。",
        ["App.axaml.cs|DisableAvaloniaDataAnnotationValidation"] =
            "Avalonia 12 里 BindingAirApps 已移除、编译型绑定默认开启，这个手动禁用步骤没有对象可禁——" +
            "体里那两行注释就是它的墓碑。留着调用点是为了下次有人问“验证插件去哪了”时看得见答案。",

        ["IMainWindowDesktopLayerService.cs|Disable"] =
            "NullMainWindowDesktopLayerService：IsSupported=false 的平台，没有桌面层可解除（Enable 侧至少留了一行日志）。",
        ["IPowerManagementService.cs|ShowNativePowerUI"] =
            "NullPowerManagementService：非 Windows 没有系统电源对话框可弹。",
        ["IWindowPassthroughServices.cs|SetupBottomMost"] =
            "NullWindowBottomMostService：非 Windows 没有置底样式位可写。",
        ["IWindowPassthroughServices.cs|SendToBottom"] =
            "同上。",
        ["IWindowPassthroughServices.cs|SetInteractiveRegions"] =
            "NullRegionPassthroughService：非 Windows 没有区域穿透（WS_EX_TRANSPARENT 那一套）可设。",
        ["IWindowPassthroughServices.cs|ClearInteractiveRegions"] =
            "同上。",
        ["UpdateProgressSubject.cs|Dispose"] =
            "UpdateProgressSubject.EmptyDisposable：给『取消一个已经不存在的订阅』返回的令牌，Dispose 就是它的全部实现。",
        ["ActionObserver.cs|OnCompleted"] =
            "ActionObserver<T>：把 IObservable 适配成一个 Action 的适配器，只关心 OnNext；完成/出错都转发给调用方自己的续体没有意义。",
        ["ActionObserver.cs|OnError"] =
            "同上。",
        ["SystemWallpaperProvider.cs|Dispose"] =
            "这个类不持有需要释放的东西——读注册表那几句是就地 using 释放的；IDisposable 只为 HostSystemWallpaperProvider 的静态实例生命周期而挂。" +
            "（顺带记着：它的 WallpaperChanged 事件全仓既不 raise 也不 subscribe，那是另一条轴的账。）",

        // —— SDK 的虚基类默认行为：轻应用不覆写就是不做事，宿主照常调用 ——
        ["AirAppWindowBase.cs|OnWindowOpened"] =
            "AirAppSdk 给第三方轻应用的 opt-in 钩子，宿主在 AirAppWindow.axaml.cs:276 调它；不覆写＝没有开窗后要做的私事。",
        ["AirAppWindowBase.cs|OnWindowClosing"] =
            "同上（覆写它可以取消关闭）。",
        ["AirAppWindowBase.cs|OnWindowClosed"] =
            "同上。",
        ["AirAppSettingsPageBase.cs|OnNavigatedTo"] =
            "同上：设置页进页钩子，多数页面只需要 XAML 绑定。",

        // —— 反序列化用的无参构造：契约是"造一个可被属性填充的对象"，空体就是全部实现 ——
        ["AirAppMarketAssetCacheService.cs|AssetCacheEntry"] =
            "AssetCacheEntry 的无参构造，给市场资产缓存 JSON 反序列化用；另一个四参构造才是代码里 new 的那条路。",

        // —— 挂账：不是"空着是对的"，是已知缺陷，等用户拍板 ——
        ["CnrDailyNewsWidget.axaml.cs|ApplyCellSize"] =
            "G1-BE / G1-BJ：签名是缩放契约，体里只把 cellSize 丢掉——29 个组件里第 2 个这样的" +
            "（另一个是 RssReaderWidget）。它连 _currentCellSize 与 UpdateAdaptiveLayout 都没有，" +
            "也就是整体不参与格子缩放；要不要跟着缩放是产品判断，等他拍。",
        ["RssReaderWidget.axaml.cs|ApplyCellSize"] =
            "G1-BE：声明了缩放契约却什么都不干——格子变大时 RSS 条目字号/行数不跟着变。怎么缩放是产品设计，等他拍。",
        ["ResumableDownloadService.cs|ResumableDownloadService"] =
            "G1-BF：构造参数 httpClient 在整个文件里被用了 0 次（ResumableDownloadService.cs:36），实际传输走 Downloader 库" +
            "（:286 的 CreateConfiguration 没有 Timeout / UserAgent 字段）。三处调用方各自设的 20s/30s/2min 超时与 UA" +
            "（AirAppMarketInstallService.cs:33、GitHubReleaseUpdateService.cs:68、UpdateOrchestrator.cs:30）对下载请求不生效。" +
            "接上会改变下载语义（大包超时失败），删掉参数则抹掉一份意图——等他拍。",
    };

    /// <summary>一行方法签名（参数表不跨行）：抓名字，并把 <c>)</c> 之后的残余留给 <see cref="IsEmptyBody"/> 判体形。</summary>
    private static readonly Regex Signature = new(
        @"^\s*(?:public|private|protected|internal)[\w<>?,\s\.]*\b(?<name>[A-Za-z_]\w*)\s*\([^()]*\)\s*(?<tail>.*)$",
        RegexOptions.Compiled);

    [Fact]
    public void EmptyImplementations_WithLiveCallers_AreAllExplained()
    {
        var repoRoot = RepoRoot();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var directory in ProductionDirectories)
        {
            var full = Path.Combine(repoRoot, directory);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Relative(repoRoot, path);
                if (relative.Split(Path.DirectorySeparatorChar)
                        .Any(part => part.Equals("obj", StringComparison.OrdinalIgnoreCase)
                            || part.Equals("bin", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (path.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sources[relative] = File.ReadAllText(path);
            }
        }

        // 覆盖面先判：语料缩了的话，下面的"零处未解释"就没有意义。
        Assert.True(
            sources.Count >= ScannedFileFloor,
            $"这一跑只扫到 {sources.Count} 个源文件（下限 {ScannedFileFloor}，2026-09-23 实测 745 个）：" +
            "目录改名/移出编译面会让这条尺子悄悄失明，先确认语料还在。");

        var detected = new List<string>();
        var discardOnly = new List<string>();

        foreach (var (relative, text) in sources)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (Signature.Match(lines[index]) is not { Success: true } signature)
                {
                    continue;
                }

                var kind = ClassifyBody(lines, index, signature.Groups["tail"].Value);
                if (kind is null)
                {
                    continue;
                }

                var name = signature.Groups["name"].Value;
                if (!HasCallSite(name, sources))
                {
                    continue;
                }

                var key = $"{Path.GetFileName(relative)}|{name}";
                detected.Add(key);
                if (kind == DiscardOnly)
                {
                    discardOnly.Add(key);
                }
            }
        }


        foreach (var anchor in MustBeDetected)
        {
            Assert.Contains(anchor, detected);
        }

        var stale = ExplainedNoops.Keys
            .Where(key => !detected.Contains(key, StringComparer.Ordinal))
            .ToList();

        // 先判陈旧：改错一个键名会同时触发"未解释"和"陈旧"，而"你名单里这条已经不存在了"才是有用的那句话。
        Assert.True(
            stale.Count == 0,
            $"名单里 {stale.Count} 条已经不是活的了：{string.Join(", ", stale.Order(StringComparer.Ordinal))}。" +
            "它对应的空实现被接上或删掉了——账本要跟着收，不然下一个人会以为那里还有个坑。");

        var unexplained = detected
            .Where(item => !ExplainedNoops.ContainsKey(item))
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            $"{unexplained.Count} 处方法体是空的却仍有调用点（事件真的发生了，做的事没有）：" +
            string.Join(", ", unexplained.Order(StringComparer.Ordinal)) +
            $"{Environment.NewLine}要么接上（它本该做的那件事在别处能找到），要么把空壳与它的调用点一起删掉" +
            "（本次就这么清了两处迁移遗留：InitializeSettingsIcons、EnsureComponentLibraryPreviewWarmup），" +
            "要么在 ExplainedNoops 里写清为什么空着是对的——说不出理由不许加，只能挂待办编号。");
    }

    private static int NextNonBlank(string[] lines, int from, string? mustBe = null)
    {
        for (var index = from; index < lines.Length; index++)
        {
            if (lines[index].Trim().Length == 0)
            {
                continue;
            }

            return mustBe is null || lines[index].Trim() == mustBe ? index : -1;
        }

        return -1;
    }

    private const string EmptyBody = "整个体为空";
    private const string DiscardOnly = "只把参数丢掉";

    /// <summary>体的种类：<see cref="EmptyBody"/>、<see cref="DiscardOnly"/>，或不是这两种（null）。</summary>
    private static string? ClassifyBody(string[] lines, int index, string tail)
    {
        if (ExtractBodyLines(lines, index, tail) is not { } bodyLines)
        {
            return null;
        }

        var statements = SplitStatements(bodyLines);

        if (statements.Count == 0)
        {
            return EmptyBody;
        }

        // "只把参数丢掉"这一格：每条语句都是 `_ = 一个不带括号的表达式`。
        // 带括号的一律不算——`_ = Foo()` 里那件事真的发生了（发后即忘的异步、忽略返回值），
        // 把它们算进来，这条判据就又开始误报，而上一版就是栽在这里。
        return statements.All(statement =>
                   statement.StartsWith("_ =", StringComparison.Ordinal)
                   && !statement.Contains('(', StringComparison.Ordinal))
            ? DiscardOnly
            : null;
    }

    /// <summary>
    /// 取方法体的**行列表**（同行、K&R 行尾开括号、Allman 三种写法都认；参数表跨行也认）。
    /// 认不出简单形状就回 null：这条判据只许往严里错（漏报），不许放宽（误报）。
    /// </summary>
    private static List<string>? ExtractBodyLines(string[] lines, int index, string tail)
    {
        if (tail.Contains("=>", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = tail.Trim();

        if (rest.Length == 0)
        {
            // Allman：签名（含跨行的参数表）之后第一个非空、非续写的行应当是 `{`。
            var open = NextNonBlank(lines, index + 1, "{");
            return open > 0 ? CollectBraced(lines, open) : null;
        }

        if (rest == "{")
        {
            return CollectBraced(lines, index);
        }

        return rest[0] == '{' && rest[^1] == '}' ? new List<string> { rest[1..^1] } : null;
    }

    /// <summary>
    /// 从开括号那行按**大括号深度**走到方法真正的收尾。
    /// 数括号用剥掉引号内文本的那一份（<c>"}"</c> 在字符串里不算括号，插值洞里的表达式仍参与），
    /// 交给判据的是原样行——上一版把两件事混在同一条字符串上做，
    /// "体首行是注释"的正常方法就被判成空体，一次报出 40 多处假红。
    /// </summary>
    private static List<string>? CollectBraced(string[] lines, int openIndex)
    {
        if (openIndex >= lines.Length || lines[openIndex].Trim() != "{")
        {
            return null;
        }

        var body = new List<string>();
        var depth = 0;

        for (var index = openIndex; index < lines.Length && index < openIndex + 80; index++)
        {
            var scannable = SourceTextScanning.WithoutStringLiteralText(lines[index]);
            foreach (var c in scannable)
            {
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                }
            }

            if (depth <= 0)
            {
                return index == openIndex ? null : body;
            }

            if (index > openIndex)
            {
                body.Add(lines[index]);
            }
        }

        return null;
    }

    /// <summary>把行列表切成语句：整行注释丢掉、行尾注释从 <c>//</c> 处截断、按 <c>;</c> 分段。</summary>
    private static List<string> SplitStatements(List<string> bodyLines)
    {
        var statements = new List<string>();

        foreach (var raw in bodyLines)
        {
            var line = raw.Trim();
            if (line.Length == 0
                || line.StartsWith("//", StringComparison.Ordinal)
                || line.StartsWith("/*", StringComparison.Ordinal)
                || line.StartsWith("*/", StringComparison.Ordinal))
            {
                continue;
            }

            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
            {
                line = line[..comment].Trim();
            }

            foreach (var piece in line.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var statement = piece.Trim();
                if (statement.Length > 0)
                {
                    statements.Add(statement);
                }
            }
        }

        return statements;
    }

    /// <summary>
    /// 除"声明"之外还能找到一处调用。逐次出现判定，不按整行：
    /// 一行 <c>private void A() =&gt; B();</c> 里 A 是声明、B 是调用，按行判会把 B 一起吃掉。
    /// 名字前面隔着空白的是类型或修饰符（<c>void Foo(</c>、<c>public Foo(</c>）→ 声明；
    /// 前面是 <c>.</c>、<c>=</c>、<c>(</c>、<c>&gt;</c> 或 <c>new</c>/<c>return</c> 这类关键字 → 调用。
    /// </summary>
    private static bool HasCallSite(string name, Dictionary<string, string> sources)
    {
        foreach (var text in sources.Values)
        {
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                for (var at = line.IndexOf(name, StringComparison.Ordinal);
                     at >= 0;
                     at = line.IndexOf(name, at + 1, StringComparison.Ordinal))
                {
                    var after = at + name.Length;
                    while (after < line.Length && line[after] is ' ' or '\t')
                    {
                        after++;
                    }

                    if (after >= line.Length || line[after] != '(')
                    {
                        continue;
                    }

                    if (at > 0 && IsIdentifierChar(line[at - 1]))
                    {
                        continue; // 命中的是更长标识符的尾巴
                    }

                    if (!IsDeclaration(line, at))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsDeclaration(string line, int at)
    {
        var cursor = at - 1;
        while (cursor >= 0 && line[cursor] is ' ' or '\t')
        {
            cursor--;
        }

        if (cursor < 0 || !IsIdentifierChar(line[cursor]))
        {
            return false;
        }

        var end = cursor;
        while (cursor >= 0 && (IsIdentifierChar(line[cursor]) || line[cursor] is '>' or ']' or ',' or '.'))
        {
            cursor--;
        }

        return !NotDeclarations.Contains(line[(cursor + 1)..(end + 1)], StringComparer.Ordinal);
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>能合法出现在"调用"前面的关键字。修饰符不在这里——<c>public Foo(</c> 是声明。</summary>
    private static readonly string[] NotDeclarations =
    [
        "new", "return", "await", "yield", "throw", "is", "as", "in", "out", "ref", "params",
        "nameof", "default", "operator", "when", "where", "let", "from", "into", "by", "on",
        "equals", "ascending", "descending", "select", "group", "checked", "unchecked", "stackalloc",
    ];

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
}
