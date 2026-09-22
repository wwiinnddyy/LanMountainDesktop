using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 实例成员零引用棘轮：非静态类型上"定义了、生产里没人调"的实例方法。
///
/// 为什么要单独一条（2026-09-22 实测的两条盲区，都不是推测）：
/// ① <see cref="ZeroUseMemberRatchetTests"/> 的口径是 <c>static class</c> 上的静态方法，
///    实例方法一条都不在它视野里；
/// ② 编译器也不管：<c>Directory.Build.props</c> 里 <c>EnforceCodeStyleInBuild=false</c>，
///    IDE0051（未使用私有成员）在构建里一条都不出。实测往宿主里塞一个零调用的 <c>private</c> 方法，
///    <c>--no-incremental</c> 重建后错误 0、IDE0051 计数 0。所以这条轴按可见性全收，连 private 一起量。
///
/// 判据（保守方向：宁可漏报，不可误删）：方法名在**生产语料**里的出现次数，减掉全语料的声明行数、
/// 再减掉"声明文件里只在字符串字面量中出现"的行数，为 0 即零引用（含 .axaml、含注释、含 nameof、
/// 含本类内部调用）。只数方法不数属性：属性能被 x:Static / Binding / 对象初始化器引用，裸名计数会把活的判成死的。
/// "声明文件里字符串不算引用"这条是从 <see cref="ZeroUseTypeRatchetTests"/> 的同名修正学来的：
/// 日志分类名与类型同名时会把计数喂饱（实测整类没被 new 的 LoadingTimeoutHandler 就是这么藏住的）。
/// 跨文件的字符串提及仍算引用，是刻意的保守——文本判据分不清 <c>Type.GetType("X")</c> 与误写。
///
/// 已知漏报方向（写在 <c>Accepted</c> 之外的地方，别扩大解读）：
/// 只被另一个死方法调用的成员进不了本棘轮；**跨二进制同名**会互相喂饱（实测 Core 的
/// <c>LanMountainDesktopIpcClient.GetCatalogAsync</c> 被 Plonds 树自带的 <c>IPlondsManifestStore.GetCatalogAsync</c>
/// 的自有调用喂成"活的"）。这类条目改由"Core / SDK 公开面"这条人工口径盯，不假装本棘轮覆盖它。
/// 终判永远是删除法：报出来只是线索，删掉重建看编译红不红。
/// </summary>
public sealed class ZeroUseInstanceMemberRatchetTests
{
    /// <summary>声明只从这些目录收，与 ZeroUseMemberRatchetTests 的 HostDirectories 同口径。</summary>
    private static readonly string[] DeclarationDirectories =
    [
        "desktop/LanMountainDesktop",
        "desktop/LanMountainDesktop.Launcher",
        "core",
        "install",
        "platform",
        "packaging",
        "scripts",
    ];

    /// <summary>
    /// 调用点要按"跨二进制可达"数。实测漏报样本：<c>RssReaderService.ProbeAsync</c> 唯一的调用点在
    /// <c>airapp/LanMountainDesktop.AirAppHost/RssReaderAirAppView.axaml.cs:241</c>，
    /// 只数声明目录会把它判成死码——删了就是破坏 SDK 宿主。
    /// </summary>
    private static readonly string[] ReachDirectories =
    [
        .. DeclarationDirectories,
        "airapp",
        "mobile",
        "PenguinLogisticsOnlineNetworkDistributionSystem",
    ];

    private static readonly string[] CorpusExtensions =
        [".cs", ".axaml", ".xaml", ".json", ".md", ".props", ".csproj", ".yml", ".yaml", ".iss", ".ps1"];

    private static readonly string[] SkipDirectoryNames =
        ["bin", "obj", ".git", ".vs", ".idea", "node_modules", "artifacts", "publish"];

    private static readonly string[] TestOnlySuffixes = ["ForTests", "ForTesting"];

    /// <summary>框架按约定回调，源码里永远数不到调用点。</summary>
    private static readonly string[] ConventionNames =
    [
        "ToString", "Equals", "GetHashCode", "GetType", "Finalize", "Dispose", "DisposeAsync",
        "CompareTo", "GetEnumerator", "MoveNext", "Reset", "OnNext", "OnCompleted", "OnError",
        "OnApplyTemplate", "OnAttachedToVisualTree", "OnDetachedFromVisualTree", "OnLoaded",
        "OnUnloaded", "Initialize", "Setup", "Teardown", "Main",
    ];

    /// <summary>
    /// 实现"本仓之外的接口"的成员名收不进 <see cref="InterfaceMemberNames"/>，只列**实测命中的**：
    /// <c>IValueConverter.ConvertBack</c>（双向绑定由框架回调）、
    /// <c>IHostApplicationLifetime.StopApplication</c>（宿主停机时由框架调）。
    /// </summary>
    private static readonly string[] ExternalInterfaceMembers =
        ["Convert", "ConvertBack", "StartApplication", "StopApplication"];

    /// <summary>源生成器会替这些特性生成调用点，源码里数不到。</summary>
    private static readonly string[] GeneratedAttributes =
        ["RelayCommand", "ObservableProperty", "JsonConstructor", "MessagePackObject"];

    private static readonly Regex Modifiers = new(
        @"^[ \t]*(?<mods>(?:(?:public|private|protected|internal|static|sealed|override|virtual|abstract|" +
        @"async|extern|unsafe|new|partial|readonly|ref)[ \t]+)+)" +
        @"(?<ret>[A-Za-z_][A-Za-z0-9_<>,\[\]\?\. ]*[ \t])?(?<name>[A-Za-z_]\w*)[ \t]*(?:<[^>\r\n]*>)?[ \t]*\(",
        RegexOptions.Compiled);

    private static readonly Regex TypeDeclaration = new(
        @"^[ \t]*(?:(?:public|private|protected|internal|static|sealed|abstract|partial|file|readonly|record)[ \t]+)*" +
        @"(?:class|struct|record|interface)[ \t]+(?<name>\w+)",
        RegexOptions.Compiled);

    private static readonly Regex InterfaceHead = new(
        @"^[ \t]*(?:(?:public|private|protected|internal|new|static|partial|unsafe|file)[ \t]+)*interface[ \t]+(?<name>\w+)",
        RegexOptions.Compiled);

    private static readonly Regex Identifier = new("[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    /// <summary>
    /// 2026-09-22 逐条读代码后判为"实现了但没有入口"的成员：每条都要写清"为什么还留着"。
    /// 名单只许缩短；条目对应的方法没了、或者被真引用了，都会红（防名单烂掉与假欠账）。
    /// </summary>
    private static readonly Dictionary<string, string> Accepted = new(StringComparer.Ordinal)
    {
        // —— 组件库分类条那一套拖拽手势（G1-AW）——
        ["MainWindow.OnComponentLibraryCategoryViewportPointerPressed"] =
            "配套字段 _isComponentLibraryCategoryGestureActive 等只被这四个方法读写；MainWindow.axaml:681 的 " +
            "ComponentLibraryCategoryViewport 存在，但四个指针事件一个都没接。删掉等于丢掉一个写完了的交互：" +
            "点击翻页是活的（OnComponentLibraryCategoryItemClick），缺的是拖动平移",
        ["MainWindow.OnComponentLibraryCategoryViewportPointerMoved"] = "同上，一套手势",
        ["MainWindow.OnComponentLibraryCategoryViewportPointerReleased"] = "同上，一套手势",
        ["MainWindow.OnComponentLibraryCategoryViewportPointerCaptureLost"] = "同上，一套手势",
        // —— 命令可用态从不重算 ——
        ["RelayCommand.RaiseCanExecuteChanged"] =
            "ICommand 不含这个方法，靠宿主在条件变化时主动调；全仓（含 .axaml）零调用＝按钮可用态从不重算。" +
            "与 G1-AU（组件 RefreshFromSettings 从不被推）同族：是缺口不是死码",
        // —— Core / SDK 公开面：删除属跨二进制破坏性变更 ——
        ["PublicIpcHostService.PublishLoadingStateAsync"] = "Core 是已发布包，删公开成员要与 SDK 版本号一起定：G1-U",
        ["LanMountainDesktopIpcClient.GetSessionInfoAsync"] = "同上，Core 公开面：G1-U",
        // —— 启动进度那条链（G1-AZ）——
        ["LoadingTimeoutHandler.SetItemTimeout"] =
            "所属类型整类没被 new（已在 ZeroUseTypeRatchetTests 名单）：超时监控 + 重试计数这套能力从没跑过",
        ["LoadingTimeoutHandler.ResetRetryCount"] = "同上，没被构造的类型里的方法",
        ["LoadingStateManager.UpdateProgress"] =
            "manager 是活的（App.axaml.cs:235 new、239 RegisterItem、240 StartItem），但全仓只注册了 " +
            "system.init 一个条目，这个推进度的入口没人调",
        ["LoadingStateManager.SetStage"] = "同上：阶段切换没人推，CurrentStage 一直停在初始值",
        ["LoadingStateManager.CheckTimeouts"] = "同上：这是超时监控的心跳，配合从没被 new 的 LoadingTimeoutHandler",
        ["LoadingStateReporter.ReportItemProgressAsync"] =
            "上报的活路径是事件驱动（LoadingStateReporter.Start 订阅 StateChanged/OverallProgressChanged），" +
            "这三个 Report*Async 是\"显式调用\"版备用入口，零调用点",
        ["LoadingStateReporter.ReportStageChangeAsync"] = "同上，事件路径已覆盖",
        ["LoadingStateReporter.ReportErrorAsync"] = "同上，事件路径已覆盖",
        // —— 考勤模块（整模块无入口，已记在类型棘轮）——
        ["AttendanceDataStore.LoadSessions"] = "所属类型已在 ZeroUseTypeRatchetTests 名单（考勤整模块无入口）：读写侧一起定",
        ["AttendanceDataStore.UpsertSession"] = "同上",
        // —— 隐私同意只写不读（G1-AY）——
        ["PrivacyAgreementService.HasUserAgreed"] =
            "写侧 SaveAgreement 是活的（OobeSessionCommitService.cs:67 落盘），但没人回头查这个位＝同意状态只写不读。" +
            "它是唯一的读取实现，删了就再看不出这里缺一道闸",
        ["PrivacyAgreementService.GetCurrentAgreementVersion"] = "同上家族：协议版本变了要不要重新征同意，没接",
        ["PrivacyAgreementService.ClearAgreement"] = "重置同意状态的能力，注释写着\"用于测试或重置\"，既没挂设置页也没挂 dev 面板",
        // —— 三条能力实现完整但界面上没入口（G1-BA）——
        ["DataStorageService.GetAvailableDiskSpaceAsync"] =
            "全仓只有这一处算 AvailableFreeSpace：\"还剩多少磁盘\"在设置/存储页没有任何地方显示",
        ["StudyDataStore.TryGetSessionReport"] =
            "按 sessionId 读单场报告，同类其它读取路径是活的：列得出历史、点不开单场报告",
        ["TimeZoneService.GetCommonTimeZones"] =
            "那张 7 个常用时区的表只在这里构造（活路径 TimeZoneService.cs:57 是按 id 解析单个时区）：没有挑的界面",
        // —— 轻应用包管理侧的断头路（G1-BB）——
        ["AirAppRuntimeService.RegisterInstalledAirAppPackageCore"] =
            "\"登记外部已放进包目录的包\"的唯一实现（ReadManifest → EnsureInstalled → 更新目录 → 标 PendingRestart）。" +
            "它原来两个公开入口实测零调用已删（活安装路径走 facade 的 InstallPackage → InstallAirAppPackageCore(375)），" +
            "于是整条能力不可达：留作能力证据，删不删与卸载功能一起定",
        ["AirAppMarketAssetCacheService.Invalidate"] =
            "注释写着\"卸载后清缓存\"，但宿主根本没有轻应用卸载路径（全仓 grep Uninstall 只有遥测事件名与启动器旧版本迁移）" +
            "＝整条能力没入口，不是漏调",
        ["AirAppMarketAirAppEntry.GetVersionSummary"] =
            "\"v版本 | API x | Host >= y\" 这行摘要只在这里构造，市场列表与详情面板都没地方显示",
        // —— 只有测试在调 ——
        ["AirAppLoader.LoadAll"] = "一次装载全部已装包的入口，生产按安装/启动时机增量装，只有 AirAppLoaderTests 在调",
        ["CompositionVisualAnimationService.TrySetOpacity"] =
            "与活的 SetOffset 同族（都走 TryApply + StopAnimation），但\"停掉动画直接落值\"这两个入口只有测试在调：" +
            "组件淡入淡出/缩放目前没走这里",
        ["CompositionVisualAnimationService.TrySetUniformScale"] = "同上，另一条轴",
    };

    /// <summary>
    /// 普查的覆盖面下限。判据被改坏时"未解释 0"会照样绿（比如正则不再匹配、语料目录少一个），
    /// 这里钉住几个必须仍在视野内的锚点：它们当中任何一个从普查里消失，就是要改这张表并写清理由。
    /// </summary>
    private static readonly string[] CensusAnchors =
    [
        "LoadingTimeoutHandler.SetItemTimeout",
        "PrivacyAgreementService.HasUserAgreed",
        "AttendanceDataStore.LoadSessions",
        "AirAppMarketAssetCacheService.Invalidate",
        "LoadingStateReporter.ReportErrorAsync",
    ];

    [Fact]
    public void ZeroUseInstanceMembers_MatchTheAcceptedList()
    {
        var repoRoot = RepoRoot();
        var reachCount = CountIdentifiers(repoRoot, ReachDirectories);
        var (declared, declaringFiles, declarationLineCount) = CollectDeclarations(repoRoot);

        var foundKeys = new HashSet<string>(StringComparer.Ordinal);
        var unexpected = new List<string>();
        var noLongerZeroUse = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, _, file, line) in declared)
        {
            seenKeys.Add(key);
            if (!foundKeys.Add(key))
            {
                continue;
                // 同名重载（如 RelayCommand 与 RelayCommand<T>）共用一条登记，第二处不再判。
            }

            var name = key[(key.LastIndexOf('.') + 1)..];
            var references = reachCount.GetValueOrDefault(name) - declarationLineCount.GetValueOrDefault(name);

            // 声明文件里"名字只出现在字符串字面量里"的行不是引用（日志分类名与成员同名的坑）。
            if (references > 0 && declaringFiles.TryGetValue(name, out var owners))
            {
                references -= StringOnlyMentions(repoRoot, owners, name);
            }

            if (references == 0 && !Accepted.ContainsKey(key))
            {
                unexpected.Add($"{key} [{file}:{line}]");
            }

            if (references > 0 && Accepted.ContainsKey(key))
            {
                noLongerZeroUse.Add(key);
            }
        }

        var stale = Accepted.Keys.Where(key => !seenKeys.Contains(key)).ToList();
        var anchorsMissing = CensusAnchors.Where(anchor => !seenKeys.Contains(anchor)).ToList();

        Assert.True(
            anchorsMissing.Count == 0,
            "这些锚点成员不再被本棘轮覆盖（正则或语料目录被改动了？）：" +
            string.Join(", ", anchorsMissing.Order(StringComparer.Ordinal)));

        Assert.True(
            unexpected.Count == 0 && stale.Count == 0 && noLongerZeroUse.Count == 0,
            $"新增零引用实例成员 {unexpected.Count} 个：{string.Join(" | ", unexpected.Order(StringComparer.Ordinal))}" +
            $"{Environment.NewLine}名单里已不存在的条目 {stale.Count} 个（删掉方法后请把登记一起删）：" +
            $"{string.Join(", ", stale.Order(StringComparer.Ordinal))}" +
            $"{Environment.NewLine}名单里已成假欠账的条目 {noLongerZeroUse.Count} 个（现在有人引用了，请删掉这条登记）：" +
            $"{string.Join(", ", noLongerZeroUse.Order(StringComparer.Ordinal))}");
    }

    /// <summary>
    /// 剥字符串这件事的两个方向都得钉住：日志分类名要剥掉（否则整类没被 new 的东西会藏住），
    /// 插值洞里的真调用不许剥掉（这条是 2026-09-22 第一版踩到的假阳性：
    /// <c>$"… {FormatRelative(entry.PublishedAt)}"</c> 被整段换掉，把活方法报成死码）。
    /// </summary>
    [Theory]
    [InlineData("AppLogger.Info(\"LoadingTimeoutHandler\", \"started\");", "LoadingTimeoutHandler", false)]
    [InlineData("Console.WriteLine($\"value {Foo.Bar()}\");", "Foo", true)]
    [InlineData("Console.WriteLine($\"plain text only\");", "Foo", false)]
    [InlineData("var x = \"Text\" + Foo();", "Foo", true)]
    public void StringLiteralStrippingKeepsInterpolationHoles(string line, string name, bool expectedVisible)
    {
        var visible = Identifier.Matches(SourceTextScanning.WithoutStringLiteralText(line))
            .Any(match => match.Value == name);
        Assert.Equal(expectedVisible, visible);
    }

    private sealed record DeclaredMember(string Key, string Name, string File, int Line);

    private static (List<DeclaredMember> Declared, Dictionary<string, List<string>> DeclaringFiles,
        Dictionary<string, int> DeclarationLineCount) CollectDeclarations(string repoRoot)
    {
        var interfaceMembers = InterfaceMemberNames(repoRoot);
        var declared = new List<DeclaredMember>();
        var declaringFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var declarationLineCount = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var directory in ReachDirectories)
        {
            foreach (var path in EnumerateFiles(repoRoot, [directory])
                         .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                         .Order(StringComparer.Ordinal))
            {
                var relative = Relative(repoRoot, path);
                var isDeclarationDirectory = DeclarationDirectories.Any(
                    host => relative.StartsWith(host.Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal));
                var lines = File.ReadAllLines(path);
                var stack = new List<string>();
                var markers = new List<int>();
                var pending = new List<string>();
                var depth = 0;

                for (var number = 0; number < lines.Length; number++)
                {
                    var raw = lines[number];
                    var code = Regex.Replace(raw, "//.*$", string.Empty);
                    var opened = code.Split('{').Length - 1;
                    var closed = code.Split('}').Length - 1;

                    var typeMatch = TypeDeclaration.Match(code);
                    var bodyless = code.TrimEnd().EndsWith(";", StringComparison.Ordinal) && opened == 0;
                    if (typeMatch.Success && !bodyless)
                    {
                        pending.Add(typeMatch.Groups["name"].Value);
                    }
                    else if (bodyless)
                    {
                        pending.Clear();
                    }

                    if (opened > 0)
                    {
                        stack.AddRange(pending);
                        foreach (var _ in pending)
                        {
                            markers.Add(depth + 1);
                        }

                        pending.Clear();
                    }

                    var match = Modifiers.Match(code);
                    if (match.Success)
                    {
                        var mods = match.Groups["mods"].Value;
                        var name = match.Groups["name"].Value;
                        var returnType = match.Groups["ret"].Value.Trim();
                        declarationLineCount[name] = declarationLineCount.GetValueOrDefault(name) + 1;

                        if (isDeclarationDirectory &&
                            IsCandidate(mods, name, returnType, code, stack, lines, number, interfaceMembers))
                        {
                            var owner = stack.Count > 0 ? string.Join('.', stack) : "<top>";
                            declared.Add(new DeclaredMember($"{owner}.{name}", name, relative, number + 1));
                            if (!declaringFiles.TryGetValue(name, out var owners))
                            {
                                owners = [];
                                declaringFiles[name] = owners;
                            }

                            if (!owners.Contains(relative))
                            {
                                owners.Add(relative);
                            }
                        }
                    }

                    depth += opened - closed;
                    while (stack.Count > 0 && depth < markers[^1])
                    {
                        stack.RemoveAt(stack.Count - 1);
                        markers.RemoveAt(markers.Count - 1);
                    }
                }
            }
        }

        return (declared, declaringFiles, declarationLineCount);
    }

    /// <summary>豁免规则：每条都对应一条实测到的假阳性来源，不豁免就是会误删活代码的尺子。</summary>
    private static bool IsCandidate(string mods, string name, string returnType, string code,
        List<string> stack, string[] lines, int number, HashSet<string> interfaceMembers)
    {
        if (Regex.IsMatch(mods, @"\b(static|override|virtual|abstract|partial|extern)\b"))
        {
            return false;
        }

        if (returnType.Length == 0 ||
            Regex.IsMatch(returnType, @"\b(class|struct|record|interface|enum)\b"))
        {
            return false;   // 构造器；以及主构造器类型的声明行（实测 internal sealed class X(IProgress<…>? p) 会被读成方法）
        }

        if (ConventionNames.Contains(name, StringComparer.Ordinal) ||
            ExternalInterfaceMembers.Contains(name, StringComparer.Ordinal) ||
            TestOnlySuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)) ||
            stack.Count > 0 && string.Equals(name, stack[^1], StringComparison.Ordinal))
        {
            return false;
        }

        if (interfaceMembers.Contains(name))
        {
            return false;
        }

        for (var cursor = number - 1; cursor >= 0; cursor--)
        {
            var attribute = lines[cursor].Trim();
            if (attribute.Length == 0 || attribute.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (!attribute.StartsWith('['))
            {
                break;
            }

            if (GeneratedAttributes.Any(token => attribute.Contains(token, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>任一 interface 块里出现过的标识符：这些名字按接口调度，不算"没人调"。</summary>
    private static HashSet<string> InterfaceMemberNames(string repoRoot)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in ReachDirectories)
        {
            foreach (var path in EnumerateFiles(repoRoot, [directory])
                         .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                var depth = 0;
                var inside = false;
                foreach (var raw in File.ReadAllLines(path))
                {
                    if (!inside)
                    {
                        inside = InterfaceHead.IsMatch(raw);
                        if (!inside)
                        {
                            continue;
                        }

                        depth = 0;
                    }

                    foreach (Match match in Identifier.Matches(Regex.Replace(raw, "//.*$", string.Empty)))
                    {
                        names.Add(match.Value);
                    }

                    depth += raw.Split('{').Length - 1;
                    depth -= raw.Split('}').Length - 1;
                    if (depth <= 0 && raw.Contains('}'))
                    {
                        inside = false;
                    }
                }
            }
        }

        return names;
    }

    private static Dictionary<string, int> CountIdentifiers(string repoRoot, IEnumerable<string> directories)
    {
        var total = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var path in EnumerateFiles(repoRoot, directories)
                     .Where(path => CorpusExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                     .Where(path => !Path.GetFileName(path).EndsWith("RatchetTests.cs", StringComparison.OrdinalIgnoreCase) &&
                                    !Path.GetFileName(path).Equals("SourceIntegrityTests.cs", StringComparison.Ordinal)))
        {
            foreach (Match match in Identifier.Matches(File.ReadAllText(path)))
            {
                total[match.Value] = total.GetValueOrDefault(match.Value) + 1;
            }
        }

        return total;
    }

    /// <summary>声明文件里"把字符串字面量拿掉就看不见这个名字"的行，不算引用。</summary>
    private static int StringOnlyMentions(string repoRoot, IEnumerable<string> declaringFiles, string name)
    {
        var skipped = 0;
        foreach (var relative in declaringFiles)
        {
            var path = Path.Combine(repoRoot, relative);
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                continue;
            }

            foreach (var raw in File.ReadAllLines(path))
            {
                var hits = Regex.Matches(raw, $@"(?<![A-Za-z0-9_]){name}(?![A-Za-z0-9_])").Count;
                if (hits == 0)
                {
                    continue;
                }

                var withoutLiterals = SourceTextScanning.WithoutStringLiteralText(raw);
                if (!Regex.IsMatch(withoutLiterals, $@"(?<![A-Za-z0-9_]){name}(?![A-Za-z0-9_])"))
                {
                    skipped += hits;
                }
            }
        }

        return skipped;
    }

    private static IEnumerable<string> EnumerateFiles(string root, IEnumerable<string> topDirs)
    {
        foreach (var dir in topDirs)
        {
            var full = Path.Combine(root, dir);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                if (Relative(root, file).Split(Path.DirectorySeparatorChar)
                        .Any(part => SkipDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase)))
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
}
