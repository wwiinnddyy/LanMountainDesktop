using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using LanMountainDesktop.Shared.IO;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// "这条路径是不是那棵树里的"只认 <c>PathContainment.IsSameOrChild</c> 一家（#G1-BI）。
///
/// 为什么裸前缀比较不行：<c>child.StartsWith(root)</c> 里 <c>root</c> 若不带分隔符，
/// <c>C:\apps\pkg</c> 会把兄弟目录 <c>C:\apps\pkg_backup</c> 里的东西算成包内——
/// 这不是理论问题，本仓三处在册点里就有：
/// <c>UpdatePathGuard.EnsurePathWithinRoot</c>（它的异常文案写着 "Path traversal detected"，
/// 而兄弟前缀正好绕过去）、<c>PlondsPayloadResolver</c> 的候选过滤、<c>AppVersionProvider</c>
/// 判"这个 exe 目录属不属于这个包根"（算错的后果是启动器挑中一个不在包根下的版本目录）。
/// 另有 5 处形似的今天没坏，因为传进去的根先过了 <c>PathSeparators.EnsureTrailingSeparator</c>——
/// **它们靠的是相邻两行的巧合，不是判据**，一旦有人把那一行简化掉就静默变坏，所以一起收进家。
///
/// 判据口径（"path-ish"启发式）：一行的接收者或第一参数里出现 <c>root</c> / <c>directory</c> /
/// <c>dir</c> 任一（不分大小写）就算候选包含判定；命中文件要么是家本身，要么必须出现在下面的
/// <see cref="Explained"/> 名单里并附理由。名单条目若不再命中也会红——登记过期同样是债。
/// 启发式必然误报"比的是目录名而不是路径"的那些点，所以名单里九条都写清了它比的到底是什么。
///
/// 大小写口径仍未决（家的注释里那条 <c>OrdinalIgnoreCase</c> 在大小写敏感文件系统上会放宽），
/// 这条守卫不解决它，只保证这个口径只有一处实现。
/// </summary>
public sealed class RawPathContainmentRatchetTests
{
    /// <summary>家本身：这两行是它唯一允许写前缀比较的地方。</summary>
    private const string HomeFile = "PathContainment.cs";

    // 2026-09-29 实测：生产语料 810 个 .cs。下限防"目录改名让判据悄悄失业"。
    private const int ScannedFileFloor = 760;

    /// <summary>
    /// 命中但**不是**路径包含判定的点：比的是目录名/清单键，不是绝对路径的父子关系。
    /// 免检按「文件 + 那一行的稳定片段」点名，**不只按文件**——一个文件里可以同时住着
    /// 一条真包含判定和一条名字前缀判定，只按文件免检会把真缺陷一起洗白
    /// （第一版就这么漏掉了 <c>AppVersionProvider.cs:228</c> 与 <c>PlondsPayloadResolver.cs:34</c> 两处，
    /// 因为同文件另有一条名字前缀判定在册）。说不出理由不许加。
    /// </summary>
    private static readonly Dictionary<string, (string Needle, string Reason)[]> Explained =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["DeploymentLayout.cs"] =
            [
                ("DeploymentDirectoryPrefix", "比的是部署目录的**名字前缀**，不是路径包含；名字没有兄弟目录歧义。"),
            ],
            ["AppVersionProvider.cs"] =
            [
                ("\"app-\"", "比的是目录名前缀 \"app-\"，判的是名字而不是父子路径。"
                    + "同文件那条真包含判定（可执行文件目录 vs 包根）必须走家，不在此列。"),
            ],
            ["PlondsPayloadResolver.cs"] =
            [
                ("ObjectsDirectoryName", "比的是清单里的**相对键**是否落在 objects/ 命名空间下，前缀里已显式带分隔符；"
                    + "同文件 34 行那条真的路径包含必须走家。"),
            ],
            ["FilesPackageInstaller.cs"] =
            [
                ("DeploymentDirectoryPrefix", "比的是相对路径开头的目录名前缀；同文件另一条真包含判定早已走家。"),
            ],
            ["InstalledProductInspector.cs"] =
            [
                ("DeploymentDirectoryPrefix", "比的是安装目录的**名字前缀**，用来挑出产品目录，不是父子路径判定。"),
            ],
            ["UwpManifestIconResolver.cs"] =
            [
                ("packageName + \"_\"", "比的是 MSIX 图标目录名是否以 \"包名_\" 开头——包名与版本之间一定有下划线，这是格式判定。"),
            ],
            ["PlondsCommitDeltaBuilder.cs"] =
            [
                ("dir + \"/\"", "发布工具链（另一份解决方案、不引用 Core）按 relative dir + 显式分隔符过滤 delta，"
                    + "比的是仓库内的相对路径键，不是本地文件系统包含。"),
                ("dir + \"\\\\\"", "同上，反斜杠那一条。"),
            ],
        };

    private static readonly Regex StartsWithCall = new(
        @"\.StartsWith\(\s*([^,)]+)", RegexOptions.Compiled);

    private static readonly Regex Pathish = new(
        @"(root|directory|dir\b|_dir|Dir)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void PathContainmentChecks_LiveInExactlyOnePlace()
    {
        var root = RepoRoot();
        var hits = new List<Hit>();
        var files = 0;

        foreach (var file in EnumerateProductionSources(root))
        {
            files++;
            var name = Path.GetFileName(file);
            foreach (var line in File.ReadLines(file)
                         .Where(l => l.Contains(".StartsWith(", StringComparison.Ordinal))
                         // 整行注释不算代码：第一版没排它，结果**解释这条改动的注释**被当成了命中
                         // （注释里写着 `target.StartsWith(root)`），判据扫的是文本，不是语义。
                         .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)))
            {
                var argument = StartsWithCall.Match(line);
                if (!argument.Success)
                {
                    continue;
                }

                var receiver = line.Substring(0, line.IndexOf(".StartsWith(", StringComparison.Ordinal));
                if (!Pathish.IsMatch(argument.Groups[1].Value) && !Pathish.IsMatch(receiver))
                {
                    continue;
                }

                hits.Add(new Hit(name, Path.GetRelativePath(root, file), line.Trim()));
            }
        }

        var unexplained = hits.FindAll(h =>
            !h.FileName.Equals(HomeFile, StringComparison.Ordinal)
            && !IsExplained(h));
        Assert.True(
            unexplained.Count == 0,
            $"用裸前缀判路径包含的点有 {unexplained.Count} 处没有理由（命中总数 {hits.Count}）："
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", unexplained.ConvertAll(h => h.Label + " -> " + h.Line))
            + Environment.NewLine + $"一律改走 {HomeFile} 的 IsSameOrChild——" +
            "root 不带分隔符时兄弟目录会被算进树里；靠相邻那行 EnsureTrailingSeparator 撑着不叫保证。");

        var stale = Explained
            .SelectMany(pair => pair.Value.Select(rule => (pair.Key, rule.Needle)))
            .Where(entry => !hits.Any(h => h.FileName.Equals(entry.Key, StringComparison.Ordinal)
                && h.Line.Contains(entry.Needle, StringComparison.Ordinal)))
            .ToList();
        Assert.True(
            stale.Count == 0,
            "免检名单里有 " + stale.Count + " 条现在根本不再命中："
            + string.Join(", ", stale.ConvertAll(s => s.Key + " 的 " + s.Needle))
            + Environment.NewLine + "要么是那条判定已经收进家了（把条目删掉），要么是启发式瞎了（那要修判据）——两种都不许留着。");

        Assert.True(
            files >= ScannedFileFloor,
            $"只扫到 {files} 个生产 .cs，下限 {ScannedFileFloor}（2026-09-29 实测 810）——" +
            "目录改名或排除规则变宽会让这条判据少看一片，那种绿不算证据");
    }

    private static bool IsExplained(Hit hit) =>
        Explained.TryGetValue(hit.FileName, out var rules)
        && rules.Any(rule => hit.Line.Contains(rule.Needle, StringComparison.Ordinal));

    private readonly record struct Hit(string FileName, string Label, string Line);

    /// <summary>
    /// 判据自己的注入点：认不出兄弟前缀的那把尺子等于没有。
    /// 这里同时钉住**家能判对**而**裸前缀会判错**那对具体形状——
    /// 换掉家的那行分隔符处理，第一格就会红。
    /// </summary>
    [Fact]
    public void SiblingPrefix_IsRejectedByTheHome_AndAcceptedByRawStartsWith()
    {
        var root = Path.Combine(Path.GetTempPath(), "LMD.Containment", Guid.NewGuid().ToString("N"));
        var sibling = root + "_backup";
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        try
        {
            var insideHome = Path.Combine(root, "bin", "app.exe");
            var insideSibling = Path.Combine(sibling, "bin", "app.exe");

            Assert.True(PathContainment.IsSameOrChild(root, insideHome));
            Assert.False(
                PathContainment.IsSameOrChild(root, insideSibling),
                "兄弟目录被算进包里——家的那条分隔符处理是这条判据的全部根据");

            // 反面对照：证明这一族确实在裸前缀下会错（不是"我以为会错"）。
            Assert.True(
                insideSibling.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase),
                "对照失效：裸前缀已经不认兄弟目录了，那家的存在意义要重新说");
        }
        finally
        {
            foreach (var dir in new[] { root, sibling })
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateProductionSources(string root)
    {
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".git", "tests", "node_modules", ".vs",
        };

        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(seg => skipped.Contains(seg)));
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

        throw new InvalidOperationException("找不到仓库根（用 LanMountainDesktop.slnx 定位）");
    }
}
