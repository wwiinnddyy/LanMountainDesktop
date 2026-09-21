using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 零使用类型棘轮。探针做法：把生产目录里所有 public/internal 声明的类型名拿全（约 1200 个），
/// 再在整仓语料（.cs/.axaml/.json/.md/.iss/.ps1 等，含同级 AirApp 与 tests）里数每个名字出现的次数，
/// 减掉"自身文本"（声明行、自身构造/方法签名、基接口列表、注释行）。剩下 0 次就是没人用的类型。
///
/// 名单只许缩短不许加长：新引入一个零使用类型会红；删掉一个欠账要连条目一起删（否则"已不存在"也会红）。
/// 每条保留理由都写在 <see cref="Accepted"/> 里，其中标"等用户拍板"的是**已实现但没有入口**的能力，
/// 不是可以顺手删掉的死码。
/// </summary>
public sealed class ZeroUseTypeRatchetTests
{
    private static readonly Dictionary<string, string> Accepted = new(StringComparer.Ordinal)
    {
        ["ProcessStartInfoArgumentExtensions"] = "扩展方法宿主，调用点写成 .WithArgument(...)，类型名本身不出现",
        ["UpdatePhaseExtensions"] = "扩展方法宿主，调用点写成 phase.IsBusy() / CanCheck()",
        ["SettingsServiceAppSnapshotExtensions"] = "扩展方法宿主，调用点写成 settings.Load() / Save()",
        ["AppWindowInitializeAppWindowPatcher"] = "Harmony 补丁，靠 [HarmonyPatch] 反射挂接",
        ["Win32WindowManagerConstructorPatcher"] = "Harmony 补丁，靠 [HarmonyPatch] 反射挂接",
        ["IWshShortcut"] = "COM 后期绑定：Type.GetTypeFromProgID(\"WScript.Shell\")，真 .lnk 能力的欠账",
        ["MainActivity"] = "Android 运行时按类型名实例化，源码里不会出现引用",
        ["IAirAppCatalogSourceProvider"] = "只作为 IAirAppCatalogSettingsService 的基接口存在（分段接口写法）",
        ["IDetachedComponentLibraryWindowService"] = "已实现但宿主内无触发点，等用户拍板，别当死码删",
        ["DetachedComponentLibraryWindowService"] = "同上：分离式组件库窗口的实现",
        ["AttendanceDataStore"] = "考勤整模块（含 AttendanceModels）无入口，等用户拍板",
        ["CompositeManifestProvider"] = "主备双清单组合器，更新路径没接线，等用户拍板",
        ["ServiceCollectionExtensions"] = "已发布 Core 包里的第二套 IPC 装配入口，删除属破坏性变更，跟 SDK 版本号一起定",
    };

    private static readonly string[] ProductionDirectories =
        ["core", "desktop", "airapp", "install", "mobile", "platform", "packaging", "scripts"];

    private static readonly string[] CorpusDirectories =
        ["core", "desktop", "airapp", "install", "mobile", "platform", "packaging", "scripts", "tests", "docs", "sample-data"];

    private static readonly string[] CorpusExtensions =
        [".cs", ".axaml", ".xaml", ".json", ".md", ".props", ".csproj", ".yml", ".yaml", ".iss", ".ps1", ".txt", ".laapp"];

    private static readonly string[] SkipDirectoryNames =
        ["bin", "obj", ".git", ".vs", ".idea", "node_modules", "artifacts"];

    private static readonly Regex Declaration = new(
        @"^[\t ]*(?:\[[^\]]*\][\t ]*)*(?:public|internal)[\t ]+(?:sealed[\t ]+|static[\t ]+|abstract[\t ]+|partial[\t ]+|readonly[\t ]+|ref[\t ]+)*(class|struct|record|interface|enum)[\t ]+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Identifier = new("[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    [Fact]
    public void ZeroUseTypes_MatchTheAcceptedList()
    {
        var repoRoot = RepoRoot();

        var declaringFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in EnumerateFiles(repoRoot, ProductionDirectories)
                     .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var relative = Relative(repoRoot, file);
            foreach (Match match in Declaration.Matches(File.ReadAllText(file)))
            {
                var name = match.Groups["name"].Value;
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

        var total = new Dictionary<string, int>(StringComparer.Ordinal);
        var selfText = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in EnumerateFiles(repoRoot, CorpusDirectories))
        {
            if (!CorpusExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var relative = Relative(repoRoot, file);
            foreach (var line in File.ReadLines(file))
            {
                foreach (Match match in Identifier.Matches(line))
                {
                    var name = match.Value;
                    if (!declaringFiles.TryGetValue(name, out var owners))
                    {
                        continue;
                    }

                    total[name] = total.GetValueOrDefault(name) + 1;
                    if (owners.Contains(relative) && IsSelfText(line, name))
                    {
                        selfText[name] = selfText.GetValueOrDefault(name) + 1;
                    }
                }
            }
        }

        var unexpected = declaringFiles
            .Where(entry => total.GetValueOrDefault(entry.Key) - selfText.GetValueOrDefault(entry.Key) <= 0)
            .Where(entry => !Accepted.ContainsKey(entry.Key))
            .Select(entry => $"{entry.Key}  ({string.Join(", ", entry.Value)})")
            .Order(StringComparer.Ordinal)
            .ToList();

        var stale = Accepted.Keys.Where(name => !declaringFiles.ContainsKey(name)).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            unexpected.Count == 0 && stale.Count == 0,
            $"新增零使用类型 {unexpected.Count} 个：{Environment.NewLine}{string.Join(Environment.NewLine, unexpected)}" +
            $"{Environment.NewLine}名单里已不存在的条目 {stale.Count} 个（探针重测后请一起删掉）：{string.Join(", ", stale)}");
    }

    /// <summary>
    /// 自身文本 = 这个类型自己的声明行、自己的构造/方法签名、把它列进基列表的行、以及任何注释行。
    /// 同文件里 <c>AddSingleton&lt;X&gt;()</c> 这类真使用不会被吞掉。
    /// </summary>
    private static bool IsSelfText(string line, string name)
    {
        var text = line.TrimStart();
        if (text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith("/*", StringComparison.Ordinal) ||
            text.StartsWith("*", StringComparison.Ordinal))
        {
            return true;
        }

        if (Regex.IsMatch(text, $@"\b(class|struct|record|interface|enum)[\t ]+{name}\b"))
        {
            return true;
        }

        // 类型自己的构造声明：访问修饰符（可带 static/sealed/override 等）后面紧跟名字再接左括号。
        // 注意别把 `public static IX GetOrCreate() => new IX();` 这类真创建也当成自述文本。
        if (Regex.IsMatch(text, $@"^(public|internal|private|protected)(\s+\w+)*\s+{name}\s*\(") &&
            !Regex.IsMatch(text, $@"[=.]{name}\s*\("))
        {
            return true;
        }

        return Regex.IsMatch(text, $@":[\t ]*{name}\b") || Regex.IsMatch(text, $@",[\t ]*{name}\b");
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
