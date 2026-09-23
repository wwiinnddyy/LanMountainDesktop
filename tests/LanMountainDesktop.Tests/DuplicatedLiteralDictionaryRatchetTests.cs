using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 第八条尺子：**字面数据表**的重复真源。
///
/// 为什么方法级的两把尺子不够：它们只比方法体。2026-09-23 收城市名那族时，
/// 世界时钟与模拟时钟各存了一份 12+12 条的"时区 id → 城市名"字典，
/// **一份写中文、一份写 \uXXXX 转义**——两份源码文本不同、又都是字段初始化而不是方法体，
/// 逐字普查与"同名不同体"普查双双失明。这类复制错了不报错，症状只是"同一个键在两块屏幕上两个值"。
///
/// 这条尺子比的是**解码后的键值集合**：先把 C# 转义（\u、\n、\" 等）还原成真实字符再取指纹，
/// 所以写法差异骗不过它。它只认 ≥3 条的字典字面量（两张三格以下的巧合表撞在一起的概率太低，
/// 且小表常是局部常量），并刻意**只看这张表自己那一层**（嵌套子表先挖掉），
/// 因为按语言分组的外层表必然与某个子表形状相似。
///
/// 覆盖面靠 <see cref="TableFloor"/> 钉住：扫到的表数低于下限就说明判据自己瞎了
/// （历史上这条轴上栽过的就是"正则把 IReadOnlyDictionary 拼错成 IReadonly，于是全仓 0 张表"）。
/// 两条自测是这条尺子的注入点：种一份转义写法相同的表必须判成同一张，
/// 差一个值必须判成不同——少了它们，"0 组重复"随时可能只是"扫不出来"。
/// </summary>
public sealed class DuplicatedLiteralDictionaryRatchetTests
{
    /// <summary>实测 8 张（城市名 3 张 + 各模块的小码表）。低于它＝判据在丢表。</summary>
    private const int TableFloor = 6;

    private const int DuplicateGroupCeiling = 0;

    /// <summary>语料里已知必须被认出来的两张表（同一内容的两种写法）。</summary>
    private static readonly string[] MustBeSeen =
    [
        @"desktop\LanMountainDesktop\Services\ClockCityNames.cs",
        @"desktop\LanMountainDesktop\Services\ClockAirApp\ClockAirAppTimeFormatter.cs",
    ];

    private static readonly string[] ProductionDirectories =
        ["core", "desktop", "airapp", "install", "platform", "mobile"];

    private static readonly string[] SkipPathParts = ["obj", "bin", "artifacts", "node_modules"];

    private static readonly Regex Constructor = new(
        @"new\s+(?:IReadOnlyDictionary|IDictionary|Dictionary)<", RegexOptions.Compiled);

    private static readonly Regex Entry = new(
        @"\[\s*""(?<k>(?:[^""\\]|\\.)*)""\s*\]\s*=\s*""(?<v>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    [Fact]
    public void IdenticalLiteralDictionaries_DoNotAppearInTwoFiles()
    {
        var repoRoot = RepoRoot();
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var scannedTables = 0;
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in EnumerateSources(repoRoot))
        {
            var relative = Relative(repoRoot, path);
            foreach (var table in ExtractTables(File.ReadAllText(path)))
            {
                scannedTables++;
                if (!groups.TryGetValue(table, out var sites))
                {
                    sites = [];
                    groups[table] = sites;
                }

                sites.Add(relative);
                seenFiles.Add(relative);
            }
        }

        Assert.True(
            scannedTables >= TableFloor,
            $"只扫到 {scannedTables} 张 ≥3 条的字典字面量，低于下限 {TableFloor}。" +
            "族数/组数为 0 也可能是判据自己瞎了：先查 Constructor/Entry 两个正则又漏了哪种写法" +
            "（历史上拼错过 IReadOnlyDictionary，也漏过嵌套子表与 { \"k\", \"v\" } 初值写法）");

        foreach (var expected in MustBeSeen)
        {
            Assert.True(seenFiles.Contains(expected), $"扫不到 {expected} 里的城市名表——这条尺子对它失明");
        }

        var duplicates = groups.Where(pair => pair.Value.Distinct(StringComparer.Ordinal).Count() >= 2).ToList();

        Assert.True(
            duplicates.Count <= DuplicateGroupCeiling,
            $"内容完全相同的字面量表出现在 {duplicates.Count} 组里（上限 {DuplicateGroupCeiling}）：\n" +
            string.Join("\n", duplicates.Select(pair => $"  {pair.Value.Distinct().Count()} 处：{string.Join(" | ", pair.Value)}")) +
            "\n同一份码表抄两处，补一处漏一处就是\"同一个键在两块屏幕上两个值\"；把它收进 Services 里的一张家表。");
    }

    /// <summary>
    /// 注入点之一：同一份表的两种写法（字面中文 vs \uXXXX 转义）必须算同一张。
    /// 这条要是红了，说明这条轴又看不见数据抄本了——正是它立项的理由。
    /// </summary>
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

        var literal = Assert.Single(ExtractTables(Literal));
        var escaped = Assert.Single(ExtractTables(Escaped));

        Assert.Equal(literal, escaped);
    }

    /// <summary>
    /// 反向对照：值差一个字符就不许算同一张表，否则"并表"会把不同的口径抹平。
    /// 也顺手钉住"只数自己那一层"：外层嵌套表与它的一个子表不许被判成重复。
    /// </summary>
    [Fact]
    public void Scanner_KeepsTablesApart_WhenOneValueDiffers_OrWhenOneIsTheOuterNesting()
    {
        const string Left = """
            class L {
                private static readonly Dictionary<string, string> T = new Dictionary<string, string>
                {
                    ["a"] = "one",
                    ["b"] = "two",
                    ["c"] = "three"
                };
            }
            """;
        const string Right = """
            class R {
                private static readonly Dictionary<string, string> T = new Dictionary<string, string>
                {
                    ["a"] = "one",
                    ["b"] = "TWO",
                    ["c"] = "three"
                };
            }
            """;
        const string Nested = """
            class N {
                private static readonly Dictionary<string, Dictionary<string, string>> T =
                    new Dictionary<string, Dictionary<string, string>>
                    {
                        ["en"] = new Dictionary<string, string>
                        {
                            ["a"] = "one",
                            ["b"] = "two",
                            ["c"] = "three"
                        }
                    };
            }
            """;

        Assert.NotEqual(Assert.Single(ExtractTables(Left)), Assert.Single(ExtractTables(Right)));

        // 外层那张"en => 子表"本身没有字面量条目（≥3 条才算），子表与 Left 的表则是同一张：
        // 这正是"按语言分组的码表被抄进两处"的真实形状。
        Assert.Equal(Assert.Single(ExtractTables(Left)), Assert.Single(ExtractTables(Nested)));
    }

    /// <summary>抽出一段源码里每张 ≥3 条字面量条目的字典，返回其内容指纹（键值集合，排序后拼接）。</summary>
    private static IEnumerable<string> ExtractTables(string text)
    {
        foreach (Match constructor in Constructor.Matches(text))
        {
            var open = text.IndexOf('{', constructor.Index);
            if (open < 0)
            {
                continue;
            }

            var depth = 0;
            var end = open;
            while (end < text.Length)
            {
                var c = text[end];
                if (c is '{' or '(')
                {
                    depth++;
                }
                else if (c is '}' or ')')
                {
                    depth--;
                    if (depth == 0 && c == '}')
                    {
                        break;
                    }
                }

                end++;
            }

            var entries = new List<string>();
            foreach (var chunk in SplitTopLevel(text[(open + 1)..end]))
            {
                var entry = Entry.Match(chunk);
                if (entry.Success)
                {
                    entries.Add($"{Unescape(entry.Groups["k"].Value)}\u2192{Unescape(entry.Groups["v"].Value)}");
                }
            }

            if (entries.Count >= 3)
            {
                entries.Sort(StringComparer.Ordinal);
                yield return string.Join(";", entries);
            }
        }
    }

    /// <summary>只在深度 0 处切逗号：嵌套子表整个落进一个片段，不会被误当成本表的条目。</summary>
    private static IEnumerable<string> SplitTopLevel(string body)
    {
        var depth = 0;
        var chunk = new System.Text.StringBuilder();

        foreach (var c in body)
        {
            if (c is '{' or '(')
            {
                depth++;
            }
            else if (c is '}' or ')')
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

        var builder = new System.Text.StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\' || index == value.Length - 1)
            {
                builder.Append(value[index]);
                continue;
            }

            var next = value[++index];

            switch (next)
            {
                case 'u' when index + 4 < value.Length:
                    builder.Append((char)Convert.ToInt32(value.Substring(index + 1, 4), 16));
                    index += 4;
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                default:
                    builder.Append(next);
                    break;
            }
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
