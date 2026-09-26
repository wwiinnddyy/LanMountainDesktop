using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 第八条尺子：**字面数据表**的重复真源。
///
/// 为什么方法级的两把尺子不够：它们只比方法体。2026-09-23 收城市名那族时，
/// 世界时钟与模拟时钟各存了一份 12+12 条的"时区 id → 城市名"字典，
/// 一份写中文、一份写转义——两份源码文本不同、又都是字段初始化而不是方法体，
/// 逐字普查与"同名不同体"普查双双失明。同类形状还有日历组件之间抄来抄去的
/// 星期表头与黄历宜忌候选池（这条尺子扩到数组形状后立刻量出 6 组，已各收进一家）。
///
/// 判据比的是**解码后的内容集合**：先把 C# 转义（\u 等）还原成真实字符再取指纹，
/// 所以"同一份表的两种写法"骗不过它。认三种形状，每种形状**单独钉覆盖面下限**——
/// 只钉一个总数的话，多认一种形状时另一种形状塌掉是看不见的。
/// 三条 Scanner_* 自测是这条轴的注入点：少了它们，"0 组重复"随时可能只是"扫不出来"
/// （初版就把 IReadOnlyDictionary 拼成 IReadonly，报出过"全仓 0 张表"的假零）。
/// </summary>
public sealed class DuplicatedLiteralDictionaryRatchetTests
{
    // 2026-09-25 实测（C# 闸门与探针两个面同为 8 / 1 / 33）：字典 indexer 写法 8 张、老式初值写法 1 张、字符串数组 33 张。
    // 2026-09-26 掉到 6 张，逐条查过才改数：ClockAirAppTimeFormatter 那份 CityNames 被删掉
    // （外层字典 1 张 + zh/ja/ko 三张内层字典 = 少 4 张），两张语言表搬进 ClockCityNames（多 2 张），
    // 8 - 4 + 2 = 6，与实测对得上；Scanner_* 四格自测照常绿，说明掉的是真表数而不是正则瞎了。
    // 用途不是查新增，是查判据自己瞎了：任何一种形状掉到下限以下＝那个形状的正则又漏写法了。
    private const int DictionaryTableFloor = 6;
    private const int OldStyleTableFloor = 1;
    private const int ArrayTableFloor = 30;

    private const int DuplicateGroupCeiling = 0;

    /// <summary>语料里已知必须被认出来的两处（掉了任何一个＝那把尺子对真实码表失明）。</summary>
    private static readonly string[] MustBeSeen =
    [
        @"desktop\LanMountainDesktop\Services\ClockCityNames.cs",
        @"desktop\LanMountainDesktop\Services\CalendarWeekLabels.cs",
        @"desktop\LanMountainDesktop\Services\LunarCalendarService.cs",
    ];

    private static readonly string[] ProductionDirectories =
        ["core", "desktop", "airapp", "install", "platform", "mobile"];

    private static readonly string[] SkipPathParts = ["obj", "bin", "artifacts", "node_modules"];

    private static readonly Regex DictionaryConstructor = new(
        @"new\s+(?:IReadOnlyDictionary|IDictionary|Dictionary)<", RegexOptions.Compiled);

    private static readonly Regex IndexerEntry = new(
        @"\[\s*""(?<k>(?:[^""\\]|\\.)*)""\s*\]\s*=\s*""(?<v>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    private static readonly Regex OldStyleEntry = new(
        @"\{\s*""(?<k>(?:[^""\\]|\\.)*)""\s*,\s*""(?<v>(?:[^""\\]|\\.)*)""\s*\}", RegexOptions.Compiled);

    private static readonly Regex StringArrayField = new(
        @"(?:string\[\]|IReadOnlyList<string>|IReadOnlyCollection<string>|IEnumerable<string>|List<string>)\s+\w+\s*=",
        RegexOptions.Compiled);

    private static readonly Regex StringLiteral = new(@"""(?:[^""\\]|\\.)*""", RegexOptions.Compiled);

    private const int MinimumEntries = 3;

    [Fact]
    public void IdenticalLiteralTables_DoNotAppearInTwoFiles()
    {
        var repoRoot = RepoRoot();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["字典 indexer"] = 0,
            ["字典老式"] = 0,
            ["字符串数组"] = 0,
        };
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var path in EnumerateSources(repoRoot))
        {
            var relative = Relative(repoRoot, path);
            var text = File.ReadAllText(path);

            foreach (var (shape, fingerprint) in ExtractTables(text))
            {
                counts[shape]++;
                seenFiles.Add(relative);
                if (!groups.TryGetValue(fingerprint, out var sites))
                {
                    sites = [];
                    groups[fingerprint] = sites;
                }

                if (!sites.Contains(relative, StringComparer.Ordinal))
                {
                    sites.Add(relative);
                }
            }
        }

        var blind = new List<string>();

        if (counts["字典 indexer"] < DictionaryTableFloor)
        {
            blind.Add($"indexer 写法只剩 {counts["字典 indexer"]} 张（下限 {DictionaryTableFloor}）");
        }

        if (counts["字典老式"] < OldStyleTableFloor)
        {
            blind.Add($"老式初值写法的字典只剩 {counts["字典老式"]} 张（下限 {OldStyleTableFloor}）");
        }

        if (counts["字符串数组"] < ArrayTableFloor)
        {
            blind.Add($"字符串数组只剩 {counts["字符串数组"]} 张（下限 {ArrayTableFloor}）");
        }

        Assert.True(
            blind.Count == 0,
            "覆盖面掉了，一次报全三种形状的实测值：" +
            $"indexer={counts["字典 indexer"]}、老式={counts["字典老式"]}、数组={counts["字符串数组"]}。" +
            "\n" + string.Join("\n", blind) +
            "\n任何一种形状静默归零，都等于那把尺子又看不见那类码表了——先修正则再往下收。");

        foreach (var expected in MustBeSeen)
        {
            Assert.True(seenFiles.Contains(expected), $"扫不到 {expected} 里的码表——这条尺子对它失明");
        }

        var duplicates = groups.Where(pair => pair.Value.Count >= 2).ToList();

        Assert.True(
            duplicates.Count <= DuplicateGroupCeiling,
            $"内容完全相同的字面量数据表出现在 {duplicates.Count} 组里（上限 {DuplicateGroupCeiling}）：\n" +
            string.Join("\n", duplicates.Select(pair => "  " + pair.Value.Count + " 处：" + string.Join(" | ", pair.Value))) +
            "\n同一份码表抄两处，补一处漏一处＝同一个键在两块屏幕上两个值；把它收进 Services 里的一张家。");
    }

    [Fact]
    public void Scanner_SeesAnEscapedTableAsTheSameTableAsALiteralOne()
    {
        const string Literal = """
            class A {
                private static readonly Dictionary<string, string> T = new Dictionary<string, string>
                {
                    ["x"] = "北京",
                    ["y"] = "伦敦",
                    ["z"] = "纽约"
                };
            }
            """;
        const string Escaped = """
            class B {
                private static readonly Dictionary<string, string> T = new Dictionary<string, string>
                {
                    ["x"] = "\u5317\u4EAC",
                    ["y"] = "\u4F26\u6566",
                    ["z"] = "\u7EBD\u7EA6"
                };
            }
            """;

        Assert.Equal(
            Assert.Single(ExtractTables(Literal)),
            Assert.Single(ExtractTables(Escaped)));
    }

    /// <summary>老式 { "k", "v" } 写法与 indexer 写法在内容相同时必须算同一张表。</summary>
    [Fact]
    public void Scanner_TreatsTheOldStyleInitializerAsTheSameTable()
    {
        const string Indexer = """
            class A {
                private static readonly Dictionary<string, string> T = new Dictionary<string, string>
                {
                    ["k1"] = "v1", ["k2"] = "v2", ["k3"] = "v3"
                };
            }
            """;
        const string OldStyle = """
            class B {
                private static readonly Dictionary<string, string> T = new Dictionary<string, string>
                {
                    { "k1", "v1" }, { "k2", "v2" }, { "k3", "v3" }
                };
            }
            """;

        Assert.Equal(
            Assert.Single(ExtractTables(Indexer)).Fingerprint,
            Assert.Single(ExtractTables(OldStyle)).Fingerprint);
    }

    /// <summary>数组的两种写法（new string[]{...} 与集合表达式）也要算同一张表。</summary>
    [Fact]
    public void Scanner_TreatsBothArraySpellingsAsTheSameTable()
    {
        const string Braced = """
            class A {
                private static readonly string[] Kinds = new string[] { "alpha", "beta", "gamma" };
            }
            """;
        const string CollectionExpression = """
            class B {
                private static readonly IReadOnlyList<string> Kinds = ["alpha", "beta", "gamma"];
            }
            """;

        var braced = Assert.Single(ExtractTables(Braced));
        Assert.Single(ExtractTables(CollectionExpression));

        Assert.Equal(braced.Fingerprint, Assert.Single(ExtractTables(CollectionExpression)).Fingerprint);
        Assert.StartsWith("数组|", braced.Fingerprint, StringComparison.Ordinal);
    }

    /// <summary>值差一个字符就不许算同一张，否则"并表"会把不同口径抹平。</summary>
    [Fact]
    public void Scanner_KeepsTablesApart_WhenOneValueDiffers()
    {
        const string Left = """
            class L {
                private static readonly Dictionary<string, string> T = new Dictionary<string, string>
                {
                    ["a"] = "one", ["b"] = "two", ["c"] = "three"
                };
            }
            """;
        const string Right = """
            class R {
                private static readonly Dictionary<string, string> T = new Dictionary<string, string>
                {
                    ["a"] = "one", ["b"] = "TWO", ["c"] = "three"
                };
            }
            """;

        Assert.NotEqual(
            Assert.Single(ExtractTables(Left)),
            Assert.Single(ExtractTables(Right)));
    }

    /// <summary>返回 (形状, 指纹)。指纹带形状前缀，形状之间不互相串组。</summary>
    private static IEnumerable<(string Shape, string Fingerprint)> ExtractTables(string text)
    {
        foreach (Match constructor in DictionaryConstructor.Matches(text))
        {
            var open = text.IndexOf('{', constructor.Index);
            if (open < 0)
            {
                continue;
            }

            var body = SliceOwnLevel(text, open, '{', '}');
            var indexer = new List<string>();
            var oldStyle = new List<string>();

            foreach (var chunk in SplitTopLevel(body))
            {
                var hit = IndexerEntry.Match(chunk);
                if (hit.Success)
                {
                    indexer.Add(Unescape(hit.Groups["k"].Value) + "→" + Unescape(hit.Groups["v"].Value));
                    continue;
                }

                hit = OldStyleEntry.Match(chunk);
                if (hit.Success)
                {
                    oldStyle.Add(Unescape(hit.Groups["k"].Value) + "→" + Unescape(hit.Groups["v"].Value));
                }
            }

            if (indexer.Count >= MinimumEntries)
            {
                yield return ("字典 indexer", Fingerprint("字典", indexer));
            }

            if (oldStyle.Count >= MinimumEntries)
            {
                yield return ("字典老式", Fingerprint("字典", oldStyle));
            }
        }

        foreach (Match field in StringArrayField.Matches(text))
        {
            var assign = field.Index + field.Length;
            var stop = text.IndexOf(';', assign);
            if (stop < 0)
            {
                continue;
            }

            // 取 = 之后到第一个 ; 的整段：new string[]{..}、[..]、new[]{..} 通吃，不做括号配对
            var items = StringLiteral.Matches(text[(assign + 1)..stop])
                .Select(literal => Unescape(literal.Value.Trim('"')))
                .ToList();

            if (items.Count >= MinimumEntries)
            {
                yield return ("字符串数组", Fingerprint("数组", items));
            }
        }
    }

    private static string Fingerprint(string shape, IEnumerable<string> items)
    {
        var sorted = items.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return shape + "|" + string.Join(";", sorted);
    }

    private static string SliceOwnLevel(string text, int openIndex, char opener, char closer)
    {
        var depth = 0;
        var index = openIndex;
        while (index < text.Length)
        {
            var c = text[index];
            if (c == opener)
            {
                depth++;
            }
            else if (c == closer)
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }
            }

            index++;
        }

        return text[(openIndex + 1)..index];
    }

    /// <summary>只在深度 0 处切逗号：嵌套子表整个落进一个片段，不会被误当成本表的一条。</summary>
    private static IEnumerable<string> SplitTopLevel(string body)
    {
        var depth = 0;
        var chunk = new StringBuilder();

        foreach (var c in body)
        {
            if (c is '{' or '(' or '[')
            {
                depth++;
            }
            else if (c is '}' or ')' or ']')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                yield return chunk.ToString();
                chunk.Clear();
                continue;
            }

            chunk.Append(c);
        }

        yield return chunk.ToString();
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\' || index == value.Length - 1)
            {
                builder.Append(value[index]);
                continue;
            }

            var next = value[++index];

            if (next == 'u' && index + 4 < value.Length)
            {
                builder.Append((char)Convert.ToInt32(value.Substring(index + 1, 4), 16));
                index += 4;
                continue;
            }

            builder.Append(next switch
            {
                'n' => "\n",
                'r' => "\r",
                't' => "\t",
                '0' => "\0",
                var other => other.ToString(),
            });
        }

        return builder.ToString();
    }

    private static IEnumerable<string> EnumerateSources(string root)
        => ProductionDirectories.SelectMany(directory =>
            {
                var baseDirectory = Path.Combine(root, directory);
                return Directory.Exists(baseDirectory)
                    ? Directory.EnumerateFiles(baseDirectory, "*.cs", SearchOption.AllDirectories)
                        .Where(path => !SkipPathParts.Any(part => path.Contains(
                            Path.DirectorySeparatorChar + part + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                    : [];
            })
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "Tests" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal));

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
