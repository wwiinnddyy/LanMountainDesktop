using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 资源所有权棘轮：谁 new 了需要释放的东西，却既不释放也不交接。
///
/// 为什么值得钉成闸门：这类缺陷不报错，只在长时间运行后显形——一个 HttpClient、一个计时器、
/// 一个还挂着的取消注册，症状是内存与句柄慢慢涨，或者"停了的服务还在回调"。
/// 2026-09-22 用脚本量过一次 543 个文件、1 处线索且已逐处读确认是交接，但脚本不在闸门里。
///
/// 判据与 <c>scripts/check-resource-ownership.py</c> 同源，两处刻意不同：
/// ① 脚本每个文件只看第一个类，这里**逐个类**算作用域（含嵌套类）——覆盖面更宽，
///    多出来的线索是脚本漏掉的，不是新引入的噪声；
/// ② <c>Dispose</c> 前不能加词边界：释放常写成 <c>CancellationHelper.CancelAndDispose(...)</c>
///    这类名字，加了会把两处正常站点报成缺陷（本轮实测踩过）。
/// 只报线索：把资源"交出去"（返回给调用方、塞进别人的构造参数、被 using 包住）在文本上很难完全区分，
/// 所以每一条命中都要读代码再判——上一轮"窗口扫描 12 命中 9 假阳性"的教训就写在这儿。
/// </summary>
public sealed class ResourceOwnershipRatchetTests
{
    /// <summary>
    /// 与脚本同口径：既列三个子目录，也列整个宿主工程目录（脚本靠一个 seen 集合去重，
    /// 这里同样要去重，否则一个文件会被数两次）。
    /// 少了"整个宿主目录"这一项时实测只扫到 329 / 543 个文件——这条判据的覆盖面下限当场把它报了。
    /// </summary>
    private static readonly string[] Directories =
    [
        Path.Combine("desktop", "LanMountainDesktop"),
        Path.Combine("desktop", "LanMountainDesktop", "Services"),
        Path.Combine("desktop", "LanMountainDesktop", "ViewModels"),
        Path.Combine("desktop", "LanMountainDesktop", "AirApps"),
        Path.Combine("core", "LanMountainDesktop.Core"),
        Path.Combine("install", "LanDesktopPLONDS.installer"),
        Path.Combine("platform", "LanMountainDesktop.Platform"),
    ];

    private static readonly Regex Created = new(
        @"\bnew\s+(HttpClient|CancellationTokenSource|DispatcherTimer|Timer|FileSystemWatcher|PeriodicTimer|" +
        @"Process|BinaryReader|MemoryStream|FileStream|StreamWriter|SemaphoreSlim)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex Released = new(
        @"Dispose|\.Close\s*\(|\.Stop\s*\(|Unsubscribe|\bDetach|\busings?\b",
        RegexOptions.Compiled);

    private static readonly Regex ClassDeclaration = new(
        @"^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)[\w \t]*?\b(?:class|struct)\s+(?<name>\w+)",
        RegexOptions.Compiled);

    /// <summary>
    /// 已逐处读过、确认"本类无物可释（资源交给调用方持有）"的拥有者类。
    /// 值要写清交给谁、活多久；交接方式一旦改掉（例如变成每请求建一个 client），这里就该重新变红。
    /// </summary>
    private static readonly Dictionary<string, string> Explained = new(StringComparer.Ordinal)
    {
        ["PlondsHttpClientFactory"] = "Create() 把 HttpClient 交回调用方：PlondsClientServiceFactory.CreateDefault " +
            "把它塞进 PlondsManifestClient / PlondsHttpPackageDownloader；CreateDefault 只在 UpdateSettingsService " +
            "构造时调一次（SettingsDomainServices.cs:2070 new UpdateSettingsService，应用级容器里的单例）" +
            "→ 一次进程一个 client，PlondsService 不实现 IDisposable 是有意的",
    };

    [Fact]
    public void EveryClassThatCreatesADisposableEitherReleasesOrHandsItOff()
    {
        var repoRoot = RepoRoot();
        var leads = new List<(string File, int Line, string ClassName, string Kind, string Raw)>();
        var scanned = 0;

        foreach (var path in EnumerateSources(repoRoot))
        {
            scanned++;
            var lines = File.ReadAllLines(path);
            foreach (var (className, start, end) in ClassSpans(lines))
            {
                var hits = new List<(int Line, string Kind, string Raw)>();
                var releases = 0;
                for (var index = start; index <= end && index <= lines.Length; index++)
                {
                    var line = lines[index - 1];
                    var created = Created.Match(line);
                    if (created.Success)
                    {
                        hits.Add((index, created.Groups[1].Value, line.Trim()));
                    }

                    if (Released.IsMatch(line))
                    {
                        releases++;
                    }
                }

                if (hits.Count == 0 || releases > 0)
                {
                    continue;
                }

                foreach (var (line, kind, raw) in hits)
                {
                    leads.Add((Relative(repoRoot, path), line, className, kind, raw));
                }
            }
        }

        var unexplained = leads.Where(lead => !Explained.ContainsKey(lead.ClassName)).ToList();
        var stale = Explained.Keys.Where(name => !leads.Any(lead => lead.ClassName == name)).ToList();

        Assert.True(
            scanned >= 500,
            $"这条判据今天只扫了 {scanned} 个文件（2026-09-22 基线 543）：范围被改窄了，先查清再放行");

        Assert.True(
            stale.Count == 0,
            "这些登记的类不再出现在线索里（交接方式改掉了？判据变了？），把登记删掉或重新核：" +
            string.Join(", ", stale.Order(StringComparer.Ordinal)));

        Assert.True(
            unexplained.Count == 0,
            "这些类 new 了需要释放的东西，却既不释放也不像交接（每条都要读代码再判，文本判据只看动词）：" +
            string.Join(" | ", unexplained.Select(lead => $"{lead.File}:{lead.Line} {lead.ClassName} 起 {lead.Kind}")
                .Order(StringComparer.Ordinal)));
    }

    /// <summary>逐个类算作用域（含嵌套类）：脚本只算每个文件的第一个类，这里覆盖得更宽。</summary>
    private static List<(string ClassName, int Start, int End)> ClassSpans(string[] lines)
    {
        var spans = new List<(string, int, int)>();
        for (var index = 0; index < lines.Length; index++)
        {
            var match = ClassDeclaration.Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            var depth = 0;
            var opened = false;
            for (var cursor = index; cursor < lines.Length; cursor++)
            {
                depth += lines[cursor].Split('{').Length - 1;
                depth -= lines[cursor].Split('}').Length - 1;
                if (lines[cursor].Contains('{'))
                {
                    opened = true;
                }

                if (opened && depth <= 0)
                {
                    spans.Add((match.Groups["name"].Value, index + 1, cursor + 1));
                    break;
                }
            }
        }

        return spans;
    }

    private static IEnumerable<string> EnumerateSources(string root)
    {
        var skip = new[] { "bin", "obj", "artifacts", "node_modules", ".git" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in Directories)
        {
            var full = Path.Combine(root, directory);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                if (Relative(root, file).Split(Path.DirectorySeparatorChar).Any(skip.Contains))
                {
                    continue;
                }

                // 三个子目录同时也在"整个宿主目录"里，不去重会把一个文件数两遍。
                if (!seen.Add(Path.GetFullPath(file)))
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
