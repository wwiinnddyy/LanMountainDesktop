using System.Text;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// XAML 资源键的"有人定义、没人引用"棘轮：把 <c>x:Key</c> 当成一种符号来查死码。
///
/// 为什么单开一条：这类键不会被编译错误抓到，也不会被 C# 侧的零使用棘轮抓到（那条只扫 C# 声明），
/// 但它是同一件事的另一半——桌面 UI 的"注册了没人要"与"要了没人注册"（G1-P 管后者）都会静默留着。
/// 一条 axaml 里的死覆盖看起来无害，实际会误导后来的人："这里有 NavigationView 的样式钩子"，
/// 而 FluentAvalonia 根本不认这个名字。
///
/// 口径与另外几条例同：宽算引用（任何文本里出现该 token 都算），
/// 只把 <c>x:Key="…"</c> 的属性值本身抠掉——同一行里的 <c>{StaticResource X}</c> 仍是引用
/// （第一版探针整行跳过，把 5 个真在用的 AirApp 颜色键报成了孤儿，这个错法当场纠正）。
///
/// 宽算引用有一条已知的乐观面：<c>tests/</c> 也在语料里，于是一条只断言「标记里必须写着这行」的
/// 文本盖章测试会把键算成使用者——设置页导航壳那 5 个覆盖此前就是这么躲过孤儿名单的。
/// 把 tests 从语料里摘掉再量一次（2026-09-30，导航壳那 5 个已改走下面那条二进制判据之后）：又多出 2 个，
/// <c>ControlCornerRadius</c>／<c>OverlayCornerRadius</c>，它们的「引用」也只是测试里的一句注释。
/// 这两族都不再靠文本口径判——<see cref="LibraryOwnedKeys"/> 逐条与随测试输出落盘的主题程序集核对，
/// 库认不认这个名字由二进制说了算，盖章测试说了不算。
/// </summary>
public sealed class UnreferencedXamlResourceKeyRatchetTests
{
    private static readonly string[] DefinitionDirectories =
        ["core", "desktop", "airapp", "install", "platform", "mobile"];

    /// <summary>
    /// 引用语料：只有"会被加载的东西"算引用。<b>故意不含 docs/</b>——
    /// 文档提一次键名不等于 UI 取用它（零使用那条棘轮就被 docs 里一份历史计划文档喂饱过一次）。
    /// </summary>
    private static readonly string[] ReferenceDirectories =
        ["core", "desktop", "airapp", "install", "platform", "mobile", "packaging", "scripts", "tests"];

    private static readonly string[] ReferenceExtensions =
        [".cs", ".axaml", ".xaml", ".json", ".resx", ".md", ".iss", ".html", ".js", ".yml", ".yaml", ".txt"];

    private static readonly Regex KeyDefinition = new(
        @"x:Key\s*=\s*""(?<key>[^""{]+)""", RegexOptions.Compiled);

    private static readonly Regex KeyToken = new(@"[A-Za-z_][\w\.]*", RegexOptions.Compiled);

    /// <summary>
    /// 今天实测 3 个没有任何引用的键。<b>只许缩不许扩</b>：每条都带查证到的理由与处置候选。
    /// 库侧键名（导航壳那五个 + 瓦片按钮那三个）不在这里——它们被 FluentAvalonia 按名取用，
    /// 且那条断言由 <see cref="LibraryOwnedKeys"/> 与二进制核对，不看这份名单。
    /// </summary>
    private static readonly Dictionary<string, string> Accepted = new(StringComparer.Ordinal)
    {
        ["AppFontFamilyJP"] =
            "中日韩字体覆盖，定义了但没人取用；接不接与 ja/ko 文案欠账同一个决定：G1-K（不代拍）",
        ["AppFontFamilyKR"] = "同上：G1-K",
        ["AirAppWindowBorderBrush"] =
            "AirAppHost 自己声明的边框画刷，全仓（含 .cs / 宿主侧）零取用；同文件里被用的只有它包的那个 Color。" +
            "窗口边框若要描边，得先决定用它还是用系统的：属外观决定，登记不动",
    };

    /// <summary>
    /// 我们标记里写死、由<b>库</b>按名字取用的键：文本口径看不见这类引用（引用在编译好的模板里），只能显式豁免。
    /// 豁免不许自称——每条都要在随测试输出落盘的那两份主题程序集里真读得到，
    /// 见 <see cref="LibraryOwnedKeys_AreAllRequestedByTheShippedThemeAssemblies"/>；
    /// 反过来这些键若从我们标记里被删掉，<see cref="LibraryOwnedKeys_AreStillDefinedInOurMarkup"/> 会红。
    /// </summary>
    private static readonly Dictionary<string, string> LibraryOwnedKeys = new(StringComparer.Ordinal)
    {
        ["NavigationViewContentBackground"] = "设置页导航壳透明覆盖（SettingsWindow.axaml）",
        ["NavigationViewContentGridBorderBrush"] = "同上",
        ["NavigationViewDefaultPaneBackground"] = "同上",
        ["NavigationViewExpandedPaneBackground"] = "同上",
        ["NavigationViewTopPaneBackground"] = "同上",
        ["PaneToggleButtonWidth"] = "导航壳瓦片按钮尺寸覆盖（NavigationStyles.axaml）",
        ["PaneToggleButtonHeight"] = "同上",
        ["PaneToggleButtonHeightGridLength"] =
            "同上。这条曾被登记成『照 WinUI 猜写的死覆盖』——那是拿 UTF-8 字节搜程序集搜出来的假阴性：" +
            "编译好的 XAML 载荷里字符串是 UTF-16，换成 UTF-16 就在库的键表里（与 NavigationViewCompactPaneLength 相邻）",
        ["ControlCornerRadius"] =
            "Avalonia 的 Fluent 主题按名取用（两份主题程序集里都读得到），4/8 是 Fluent 设计档，" +
            "GlassModule 与启动器三个窗口各覆盖一份。此前它的『引用』只有测试里那句注释",
        ["OverlayCornerRadius"] = "同上",
    };

    /// <summary>
    /// 反向对照：同一族里<b>库不认</b>的名字，探针必须搜不到。
    /// 三个都是照 WinUI 的资源名猜写的（2026-09-30 删掉），最后一个与库里真名只差一个词——
    /// 库里那条叫 <c>NavigationViewItemOnLeftIconBoxHeight</c>，写成 <c>NavigationViewItem…</c> 就静默不生效。
    /// 哪天真搜得到，说明库补了这套名字，那三行覆盖的去处要重新判一次。
    /// </summary>
    private static readonly string[] KeysTheThemeAssembliesDoNotMention =
    [
        "NavigationViewPaneBackground",
        "NavigationViewMinimalPaneBackground",
        "NavigationViewItemIconBoxHeight",
    ];

    private static readonly string[] ThemeAssemblies =
        ["FluentAvalonia.dll", "Avalonia.Themes.Fluent.dll"];

    /// <summary>
    /// 库的 XAML 键表能不能读到，只能靠已知正例校——2026-09-23 那次「包里 grep 不到这个名字」的结论
    /// 是用 UTF-8 搜整份程序集搜出来的，而那种搜法对<b>任何</b>键名都报 0（拿我们自己那份 DLL 试同样报 0），
    /// 于是它把 1 条真键（<c>PaneToggleButtonHeightGridLength</c>）连同 5 个真的导航壳覆盖一起说成了死覆盖。
    /// 这条格每次跑都做两个方向：登记为库有的必须搜得到，登记为库没有的必须搜不到。
    /// </summary>
    [Fact]
    public void LibraryOwnedKeys_AreAllRequestedByTheShippedThemeAssemblies()
    {
        var payloads = new List<byte[]>();
        var missing = new List<string>();
        foreach (var assembly in ThemeAssemblies)
        {
            var path = Path.Combine(AppContext.BaseDirectory, assembly);
            if (!File.Exists(path))
            {
                missing.Add(assembly);
                continue;
            }

            payloads.Add(File.ReadAllBytes(path));
        }

        Assert.True(
            missing.Count == 0,
            "测试输出目录里找不到主题程序集 " + string.Join(", ", missing.Order(StringComparer.Ordinal)) +
            "：看不见库的键表时这条豁免无从校验，此时的绿不算证据");

        var unverified = LibraryOwnedKeys.Keys
            .Where(key => !payloads.Any(content => MentionsThemeKey(content, key)))
            .ToList();

        Assert.True(
            unverified.Count == 0,
            "登记为『库按名取用』却在两份主题程序集里搜不到的键：" +
            string.Join(", ", unverified.Order(StringComparer.Ordinal)) +
            "。要么库不再认它（那这条覆盖已经失效，该删或换名），要么探针的编码假设变了");

        var stillBlind = KeysTheThemeAssembliesDoNotMention
            .Where(key => payloads.Any(content => MentionsThemeKey(content, key)))
            .ToList();

        Assert.True(
            stillBlind.Count == 0,
            "反向对照失效：库的键表现在搜得到 " + string.Join(", ", stillBlind.Order(StringComparer.Ordinal)) +
            "。可能是库补了这套名字（那三行删掉的覆盖要重新判去处），也可能是这条搜法被放宽到能匹配任何东西——" +
            "先分清是哪一种再动断言");
    }

    /// <summary>
    /// 豁免名单的另一半：库里真认的名字，仓库这一侧必须还在定义。
    /// 少了这一条，删掉一行透明覆盖只有编译知道——而编译根本不检查 <c>x:Key</c>。
    /// </summary>
    [Fact]
    public void LibraryOwnedKeys_AreStillDefinedInOurMarkup()
    {
        var repoRoot = RepoRoot();
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in DefinitionDirectories)
        {
            foreach (var path in EnumerateFiles(repoRoot, directory, [".axaml", ".xaml"]))
            {
                foreach (var raw in File.ReadLines(path))
                {
                    foreach (Match match in KeyDefinition.Matches(raw))
                    {
                        defined.Add(match.Groups["key"].Value.Trim());
                    }
                }
            }
        }

        var gone = LibraryOwnedKeys.Keys.Where(key => !defined.Contains(key)).ToList();

        Assert.True(
            gone.Count == 0,
            "登记为『库按名取用』的键在仓库标记里已经没有定义了：" +
            string.Join(", ", gone.Order(StringComparer.Ordinal)) +
            "。这些覆盖是会真改到外观的（设置页导航壳透明、瓦片按钮 40×40），" +
            "要删就得连这条豁免一起删，并说清外观按什么接管");
    }

    private static bool MentionsThemeKey(byte[] assemblyContent, string key)
        => assemblyContent.AsSpan().IndexOf(Encoding.Unicode.GetBytes(key)) >= 0;

    [Fact]
    public void XamlResourceKeys_ThatNobodyReferences_DoNotGrow()
    {
        var repoRoot = RepoRoot();
        var definitions = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var directory in DefinitionDirectories)
        {
            foreach (var path in EnumerateFiles(repoRoot, directory, [".axaml", ".xaml"]))
            {
                var relative = Relative(repoRoot, path);
                foreach (var (line, raw) in Numbered(File.ReadAllLines(path)))
                {
                    foreach (Match match in KeyDefinition.Matches(raw))
                    {
                        var key = match.Groups["key"].Value.Trim();
                        if (key.Length == 0 || key.StartsWith("resm:", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!definitions.TryGetValue(key, out var sites))
                        {
                            sites = [];
                            definitions[key] = sites;
                        }

                        sites.Add($"{relative}:{line}");
                    }
                }
            }
        }

        var references = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in ReferenceDirectories)
        {
            foreach (var path in EnumerateFiles(repoRoot, directory, ReferenceExtensions))
            {
                foreach (var raw in File.ReadLines(path))
                {
                    // 只把 x:Key 的属性值本身抠掉：那是定义，不是使用。
                    var stripped = KeyDefinition.Replace(raw, "x:Key=\"\"");
                    foreach (Match match in KeyToken.Matches(stripped))
                    {
                        references.Add(match.Value);
                    }
                }
            }
        }

        var foundKeys = new HashSet<string>(StringComparer.Ordinal);
        var unexpected = new List<string>();

        foreach (var (key, sites) in definitions)
        {
            if (references.Contains(key) || LibraryOwnedKeys.ContainsKey(key))
            {
                continue;
            }

            foundKeys.Add(key);
            if (!Accepted.ContainsKey(key))
            {
                unexpected.Add($"{key}（定义在 {string.Join(", ", sites)}）");
            }
        }

        var stale = Accepted.Keys.Where(key => !foundKeys.Contains(key)).ToList();

        Assert.True(
            definitions.Count >= 100,
            $"这一跑只看到 {definitions.Count} 个 x:Key 定义（2026-09-23 实测 104 个）：" +
            "定义正则或扫描范围坏了，此时的\"孤儿数\"不可信");

        Assert.True(
            unexpected.Count == 0 && stale.Count == 0,
            $"新增没人引用的 XAML 资源键 {unexpected.Count} 个（要么接上取用点，要么删掉定义；确属有意保留就带理由登记）：" +
            string.Join(", ", unexpected.Order(StringComparer.Ordinal)) +
            $"{Environment.NewLine}名单里已对不上的键 {stale.Count} 个（两种原因都会落在这里：键的定义被删了，" +
            "或者它现在真的有人取用了——后者说明这条豁免是假欠账，都请把条目一起删掉）：" +
            string.Join(", ", stale.Order(StringComparer.Ordinal)));
    }

    private static IEnumerable<(int Line, string Text)> Numbered(IEnumerable<string> lines)
    {
        var number = 0;
        foreach (var line in lines)
        {
            yield return (++number, line);
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, string directory, string[] extensions)
    {
        var full = Path.Combine(root, directory);
        if (!Directory.Exists(full))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            if (Relative(root, file).Split(Path.DirectorySeparatorChar)
                    .Any(part => part.Equals("obj", StringComparison.OrdinalIgnoreCase)
                        || part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                        || part.Equals("artifacts", StringComparison.OrdinalIgnoreCase)
                        || part.Equals("node_modules", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (extensions.Any(extension => file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            {
                // 名单文件自己不算使用者：登记条目里写的就是这些键名，
                // 不排掉的话每条豁免都会自证"有人引用"（零使用那几条棘轮同一条教训）。
                if (file.EndsWith("RatchetTests.cs", StringComparison.OrdinalIgnoreCase))
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
