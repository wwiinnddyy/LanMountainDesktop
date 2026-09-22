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
    /// 今天实测 7 个没有任何引用的键。<b>只许缩不许扩</b>：每条都带查证到的理由与处置候选。
    /// 库侧键名（PaneToggleButtonWidth/Height）不在这里——它们被 FluentAvalonia 按名取用，见
    /// <see cref="LibraryOwnedKeys"/>。
    /// </summary>
    private static readonly Dictionary<string, string> Accepted = new(StringComparer.Ordinal)
    {
        ["AppFontFamilyJP"] =
            "中日韩字体覆盖，定义了但没人取用；接不接与 ja/ko 文案欠账同一个决定：G1-K（不代拍）",
        ["AppFontFamilyKR"] = "同上：G1-K",
        ["AirAppWindowBorderBrush"] =
            "AirAppHost 自己声明的边框画刷，全仓（含 .cs / 宿主侧）零取用；同文件里被用的只有它包的那个 Color。" +
            "窗口边框若要描边，得先决定用它还是用系统的：属外观决定，登记不动",
        ["NavigationViewPaneBackground"] =
            "只在 SettingsWindow.axaml 里自己定义，仓库内无人引用，FluentAvalonia 与 Avalonia 主题包里都 grep 不到这个名字" +
            "（2026-09-23 实测 0 命中）——像是照 WinUI 的资源名猜写的死覆盖",
        ["NavigationViewMinimalPaneBackground"] = "同上：库与仓库都搜不到取用点",
        ["NavigationViewItemIconBoxHeight"] = "同上：库与仓库都搜不到取用点",
        ["PaneToggleButtonHeightGridLength"] =
            "<c>PaneToggleButtonHeight</c> 是真的（库按名取用），这个 GridLength 版本不是：库里 grep 不到，仓库里也没人引用",
    };

    /// <summary>库自己按名字取用的键：不是孤儿，但文本口径看不见（引用在编译好的样式里），所以显式豁免。</summary>
    private static readonly Dictionary<string, string> LibraryOwnedKeys = new(StringComparer.Ordinal)
    {
        ["PaneToggleButtonWidth"] =
            "FluentAvalonia 的 NavigationView 样式按名取用（2026-09-23 在 ~/.nuget/packages/fluentavaloniaui 里 grep 到 8 个文件命中）",
        ["PaneToggleButtonHeight"] = "同上（4 个文件命中）",
    };

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
