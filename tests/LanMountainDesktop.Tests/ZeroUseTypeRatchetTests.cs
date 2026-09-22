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
        ["ManifestSignatureVerifier"] = "实现了但安装流程没调用（只有 InstallerSecurityTests 在用）：清单 RSA-PSS 校验没接线，" +
            "且内置公钥是空串（IsConfigured=false 时 Verify 直接返回 true）——接线与内置公钥都要用户拍板",
        ["AuthenticodeVerifier"] = "实现了但安装流程没调用：WinVerifyTrust 校验没接进落盘前那道关，" +
            "EnforcementEnabled 读环境变量也没人问——接线与否要用户拍板",
    };

    private static readonly string[] ProductionDirectories =
        ["core", "desktop", "airapp", "install", "mobile", "platform", "packaging", "scripts"];

    /// <summary>
    /// "有人用"只认两种证据：生产目录里的真调用点，以及 <c>tests/ApprovalFiles</c>（已发布 SDK 公共面的契约快照）。
    /// 2026-09-22 改的口径：原来把整个 tests 与 docs 算进语料，于是"只有测试在调"和"历史计划文档提过一句"
    /// 都成了使用者——实测就是把 <c>AuthenticodeVerifier</c> / <c>ManifestSignatureVerifier</c>
    /// 这两个生产零调用的安全校验器看成了活的（测试全绿，但安装流程根本没调它们）。
    /// </summary>
    private static readonly string[] CorpusDirectories =
        ["core", "desktop", "airapp", "install", "mobile", "platform", "packaging", "scripts",
            "tests/LanMountainDesktop.Tests/ApprovalFiles"];

    private static readonly string[] CorpusExtensions =
        [".cs", ".axaml", ".xaml", ".json", ".md", ".props", ".csproj", ".yml", ".yaml", ".iss", ".ps1", ".txt", ".laapp"];

    private static readonly string[] SkipDirectoryNames =
        ["bin", "obj", ".git", ".vs", ".idea", "node_modules", "artifacts"];

    /// <summary>
    /// 名单文件自己不许算作"使用者"：Accepted 里写的就是这些类型名，
    /// 把它们算进语料会让每条登记都变成"有人用"——探针把自己写的答案当成了证据。
    /// 2026-09-22 加这条时实测：不排除的话，13 条登记项全部显示为已被引用，
    /// 而且同一污染会让"只被测试引用"的类型从棘轮里漏掉。
    /// </summary>
    private const string SelfReferencingFileSuffix = "RatchetTests.cs";

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
            if (file.EndsWith(SelfReferencingFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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

        // 反向：名单里的条目若已经被真引用了（探针不再判它零使用），它就是假欠账——
        // 留着会让下一次复查以为"这条已经查过了"。两种过期都要红。
        var noLongerZeroUse = declaringFiles.Keys
            .Where(name => Accepted.ContainsKey(name)
                           && total.GetValueOrDefault(name) - selfText.GetValueOrDefault(name) > 0)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unexpected.Count == 0 && stale.Count == 0 && noLongerZeroUse.Count == 0,
            $"新增零使用类型 {unexpected.Count} 个：{Environment.NewLine}{string.Join(Environment.NewLine, unexpected)}" +
            $"{Environment.NewLine}名单里已不存在的条目 {stale.Count} 个（探针重测后请一起删掉）：{string.Join(", ", stale)}" +
            $"{Environment.NewLine}名单里已成假欠账的条目 {noLongerZeroUse.Count} 个（现在有人用了，请删掉这条登记）：{string.Join(", ", noLongerZeroUse)}");
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

        return Regex.IsMatch(text, $@":[\t ]*{name}\b")
               // 只把"换行续写的基列表/参数列表"（分隔符在行首）当自述。
               // 原来这里是找 `,\s*名字`，结果把泛型实参也吞了：
               // `IReadOnlyDictionary<string, PlondsClientChangedFileEntry> ChangedFilesMap,` 这种
               // 真使用被当成自述，于是清单条目类型全被误判成零使用（2026-09-22 实测，两个 ChangedFileEntry）。
               || Regex.IsMatch(text, $@"^[,:][\t ]*{name}\b");
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
