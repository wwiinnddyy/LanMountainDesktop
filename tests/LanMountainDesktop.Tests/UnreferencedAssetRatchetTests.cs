using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 图片/字体/图标这类资源不会出现在编译错误里，也不会有人 grep 它——放进 <c>Assets/</c> 就会被
/// 打进安装包里长期占地方。这条棘轮的做法与零使用类型那条例同：把生产目录里的资源名拿去整仓文本里找，
/// 找不到的就是孤儿。<see cref="AcceptedUnreferencedDirectories"/> 里登记的目录是已知欠账（带理由），
/// 只许缩不许扩；往 <c>Assets/</c> 塞一张没人用的图会立刻红。
/// </summary>
public sealed class UnreferencedAssetRatchetTests
{
    private static readonly string[] ProductionDirectories =
        ["core", "desktop", "airapp", "install", "mobile", "platform", "packaging", "scripts"];

    private static readonly string[] CorpusDirectories =
        [.. ProductionDirectories, "tests", "docs", "sample-data", ".trae"];

    private static readonly string[] AssetExtensions =
        [".png", ".ico", ".svg", ".jpg", ".jpeg", ".bmp", ".gif", ".ttf", ".woff", ".woff2", ".icns"];

    private static readonly string[] TextExtensions =
        [".cs", ".axaml", ".xaml", ".csproj", ".json", ".iss", ".ps1", ".props", ".yml", ".yaml", ".md", ".txt", ".laapp", ".html", ".js", ".xml", ".resx", ".config", ".ini"];

    private static readonly string[] SkipDirectoryNames =
        ["bin", "obj", ".git", ".vs", ".idea", "node_modules", "artifacts"];

    /// <summary>已知没人用的资源目录，值是想删但要用户拍板的理由；配额是"就这么大，别再往里塞"。</summary>
    private static readonly Dictionary<string, (int Files, long Kibibytes, string Reason)> AcceptedUnreferencedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [@"desktop\LanMountainDesktop\Assets\endfiled"] =
                (24, 1303,
                    "24 张表情图共 1.27MB，全仓（含 .axaml / .cs / 安装包脚本 / 同级 AirApp / 规格文档）零引用，"
                    + "也没有任何代码按目录枚举它们。删属产品决定，先冻结数量与体积不再扩大。"),
        };

    /// <summary>顶层容器目录：里面的东西都是逐个按文件名引用的，"目录被枚举"这条判据对它们没意义。</summary>
    private static readonly string[] GenericContainerDirectories =
        ["Assets", "Resources", "Images", "Icons", "Media", "Fonts"];

    [Fact]
    public void UnreferencedAssets_AreOnlyTheAcceptedOnes()
    {
        var repoRoot = RepoRoot();
        var offenders = new List<string>();
        var accepted = new Dictionary<string, (int Files, long Bytes)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in ProductionDirectories
                     .Select(part => Path.Combine(repoRoot, part))
                     .Where(Directory.Exists)
                     .SelectMany(directory => EnumerateFiles(directory))
                     .Where(file => AssetExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)))
        {
            if (IsReferenced(file))
            {
                continue;
            }

            var relative = Path.GetRelativePath(repoRoot, file);
            var directory = Path.GetDirectoryName(relative) ?? string.Empty;
            if (AcceptedUnreferencedDirectories.TryGetValue(directory, out var quota))
            {
                accepted.TryGetValue(directory, out var tally);
                accepted[directory] = (tally.Files + 1, tally.Bytes + new FileInfo(file).Length);
                continue;
            }

            offenders.Add(relative);
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 个资源文件放进仓库却全仓没人引用（会被打进安装包）："
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, offenders.OrderBy(x => x, StringComparer.Ordinal).Take(30))}"
            + $"{Environment.NewLine}用掉它、删掉它，或者确属已知欠账再加进名单并写清理由。");

        foreach (var entry in AcceptedUnreferencedDirectories)
        {
            var seen = accepted.TryGetValue(entry.Key, out var tally) ? tally : default;
            Assert.True(
                seen.Files > 0,
                $"免检名单里的 {entry.Key} 已经没有孤儿资源了，把这条名单删掉。");
            Assert.True(
                seen.Files <= entry.Value.Files && seen.Bytes <= entry.Value.Kibibytes * 1024,
                $"{entry.Key} 的孤儿资源从 {entry.Value.Files} 个 / {entry.Value.Kibibytes} KiB 涨到了 "
                + $"{seen.Files} 个 / {seen.Bytes / 1024} KiB。理由：{entry.Value.Reason}");
        }
    }

    /// <summary>
    /// 宽松口径：文件名整串出现在任何文本里算引用（<c>avares://…/logo.svg</c>、csproj 的 glob、
    /// 测试断言、文档里提一嘴都算）；文件名主干长度够长时按整词匹配也算（字体是按家族名引用的，
    /// <c>MiSans-VF.ttf</c> 要靠 <c>#MiSans</c> 那类名字命中）。这两条扫全仓语料。
    /// 第三条"代码里出现了这个目录的路径"只扫生产目录——否则本测试的免检理由里写了那个目录名，
    /// 守卫就会被自己的注释喂成永真。目录名必须是作为路径一段出现的（<c>/endfiled</c>、
    /// <c>endfiled/</c>、<c>"endfiled"</c>），因为按目录枚举资源的代码总是拼路径；
    /// 光出现 <c>Assets</c> 这种顶层容器名不算，否则 <c>Assets/</c> 下能塞任意死图。
    /// </summary>
    private static bool IsReferenced(string file)
    {
        var name = Path.GetFileName(file);
        var stem = Path.GetFileNameWithoutExtension(file);
        var directory = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);

        var corpus = CorpusText(CorpusDirectories);
        if (corpus.Contains(name, StringComparison.Ordinal))
        {
            return true;
        }

        if (stem.Length >= 8 && WholeWord(stem).IsMatch(corpus))
        {
            return true;
        }

        return directory.Length > 0
            && !GenericContainerDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase)
            && FolderPathMentions(directory).IsMatch(CorpusText(ProductionDirectories));
    }

    private static Regex FolderPathMentions(string directory) =>
        new($@"""{Regex.Escape(directory)}(\b|[/\\])|[/\\]{Regex.Escape(directory)}(\b|[/\\])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> CorpusCache = new(StringComparer.Ordinal);

    private static string CorpusText(string[] parts)
    {
        var key = string.Join("|", parts);
        if (CorpusCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var repoRoot = RepoRoot();
        var builder = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            var directory = Path.Combine(repoRoot, part);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in EnumerateFiles(directory)
                         .Where(file => TextExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)))
            {
                builder.Append(File.ReadAllText(file)).Append('\n');
            }
        }

        var corpus = builder.ToString();
        CorpusCache[key] = corpus;
        return corpus;
    }

    private static Regex WholeWord(string value) =>
        new($@"\b{Regex.Escape(value)}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static IEnumerable<string> EnumerateFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar)
                .Any(part => SkipDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase)));

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

        throw new InvalidOperationException("找不到仓库根。");
    }
}
