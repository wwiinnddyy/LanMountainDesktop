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
    /// <summary>今天实测：86 组逐字相同的方法体。只能降，要升必须在这里写清理由。</summary>
    private const int IdenticalBodyFamilyCeiling = 86;

    /// <summary>
    /// 今天实测：193 个方法名存在 ≥2 种体。只能降，要升必须在这里写清理由。
    /// 同一棵树上 <c>dump-drift-methods.py</c> 报的是 196，差的 3 族**没有逐条对过**；
    /// 最可能的来源是脚本把"没有方法体的声明"（接口方法、抽象方法）也当成一种体，而这里跳过空体
    /// （见 NormalizeBody 返回空串时的 continue）。这条差异记在这里，不当结论用。
    /// </summary>
    private const int DriftFamilyCeiling = 193;

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
        @"(?:[A-Za-z_][\w<>\[\]?,\. ]*?\s+)?(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\([^)]*\)\s*(?:=>.*)?\{?\s*$",
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
                if (body.Count < 2)
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
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (!bodiesByName.TryGetValue(name, out var tally))
                {
                    tally = new NameTally();
                    bodiesByName[name] = tally;
                }

                tally.Record(relative, normalized);
            }
        }

        // 与脚本同门槛：≥3 处声明、≥2 种体、且横跨 ≥3 个文件，才算"同名不同体"要收口的族。
        var driftFamilies = bodiesByName.Count(pair => pair.Value.VariantCount >= 2 &&
                                                      pair.Value.Sites >= 3 &&
                                                      pair.Value.Files >= 3);
        AssertEqual(
            DriftFamilyCeiling,
            driftFamilies,
            "同名不同体的漂移族数变了：变多说明同一个名字又多了一种实现（这正是「家被绕开」的形态，" +
            "先收口或写理由改上限）；变少是好事，把常量改成新的数就是收口的记账");
    }

    /// <summary>与脚本一致：折叠空白、字符串字面量换成占位符，所以"只差一句文案"不算第二种体。</summary>
    private static string NormalizeBody(string[] lines, int signatureIndex)
    {
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

        if (!started)
        {
            var signature = lines[signatureIndex].TrimEnd();
            if (!signature.EndsWith(";", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var arrow = signature.IndexOf("=>", StringComparison.Ordinal);
            body = arrow < 0 ? [] : [signature[(arrow + 2)..].Trim()];
        }

        var normalized = Whitespace.Replace(string.Join(" ", body), " ");
        return StringLiteral.Replace(normalized, "\"S\"").Trim();
    }

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
