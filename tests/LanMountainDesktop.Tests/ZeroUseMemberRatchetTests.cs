using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 静态类零引用成员棘轮。IDE0051 只判 private 成员，<c>internal static class</c> 上的
/// <c>public static</c> 方法它不看，宿主里就藏着"看着像 API 其实没人调"的第二真源与绕闸入口
/// （<c>ComponentChromeCornerRadiusHelper</c> 一次就量出 6 个零调用成员、其中三对互为别名）。
///
/// 做法与 <see cref="ZeroUseTypeRatchetTests"/> 一致：把各生产目录里所有静态类上的
/// public/internal 静态方法拿全，在产品可达语料里数方法名出现次数并减掉声明行与注释行，
/// 为 0 即零引用。名单只许缩短，条目对应的方法没了也会红（防名单烂掉）。
/// 先只做方法：静态属性能被 x:Static 引用，方法不能。
///
/// 口径从 1 个目录（宿主）扩到 7 个（+ Launcher / Core / 安装器 / Platform / Packaging / Scripts）：
/// 扩完当场量出 21 条零引用成员，逐条判完＝删 10 条（纯备胎：单行包装、没人读的便捷谓词、
/// 一处都没调的 HEAD 续传探测）、改接线 4 条（<c>UpdatePaths</c> 的 4 个访问器是活的，
/// 绕开它们的宿主内联复制已合并）、登记 7 条（下面 <see cref="Accepted"/> 的 7 条新条目全部挂在用户待办上）。
///
/// 已知盲区（2026-09-22 实测出来的）：计数按**裸方法名**在语料里出现次数算，所以名字越常用越容易被
/// 无关文本"喂饱"——<c>Save</c> / <c>Load</c> 这类成员就算零调用也报不出来。
/// 当天 <c>SettingsServiceAppSnapshotExtensions.Save()</c> 就是靠"删掉方法后全解决方案仍 0 错误"才判死的。
/// 名单里的条目要复核时别只信这条探针，删除法（编译红不红）才是终判。
/// </summary>
public sealed class ZeroUseMemberRatchetTests
{
    /// <summary>
    /// 每条都要写清"为什么还留着"。前 3 条是宿主内**要用户拍板的能力**；
    /// 2026-09-22 口径扩到 7 个目录后又加了 7 条，全部挂在待办编号上，不是可以顺手删的死码：
    /// 删掉的 10 条＝<c>UpdatePhaseExtensions.IsTerminal</c>、<c>ThemeService.ApplyLightTheme/ApplyDarkTheme</c>、
    /// <c>DotNetRuntimeProbe.FindDotNetHostPath</c>、<c>LauncherBackgroundService.FindManagedImageFile</c>、
    /// <c>LauncherDebugSettingsStore.SaveDevModeState/SaveCustomHostPath</c>（活的写盘点是 ErrorWindow/SplashWindow
    /// 各自 new 整条记录，这两个部分更新包装从没被用）、<c>ResilientDownloader.CheckRangeSupportAsync</c>
    /// （续传真检测靠 ranged GET 的响应码，这个 HEAD 探测零调用）、
    /// <c>AppVersionProvider.ResolveFromDeploymentDirectory</c>、<c>LauncherRuntimeMetadata.HasOption</c>。
    /// </summary>
    private static readonly Dictionary<string, string> Accepted = new(StringComparer.Ordinal)
    {
        ["ComponentPlacementRules.CanPlaceInStatusBar"] = "比 ComponentRegistry.AllowsStatusBarPlacement 多一条 height==1 约束，live 用的是注册表版："
            + "这条约束从来没生效过，但它是要不要限高这个设计意图的唯一证据，删之前先问",
        ["WindowsNativeDialogService.ShowInformation"] = "原生提示框未被用（宿主用自己的对话框）：待决",
        ["XiaomiWeatherCodeMapper.ResolveBucket"] = "WeatherConditionBucket 那 11 档在宿主内除本文件外零引用："
            + "整个按天气状况分档的维度没接进 UI，属实现了但没入口，接线与否是产品决定",
        ["WindowsShortcutWriter.TryCreateShortcut"] = "真 .lnk 写入器已实现且完整，安装器一处都没调（只交付 .url）：待办 G1-J",
        ["AuthenticodeVerifier.VerifyFile"] = "Authenticode 校验器只有测试在调，生产零调用：待办 G1-AM",
        ["ManifestSignatureVerifier.GetSignatureUrl"] = "清单签名校验同上（.sig URL 拼法只被测试用）：待办 G1-AM",
        ["ServiceCollectionExtensions.AddLanMountainDesktopIpcHost"] = "Core 已发布包里的第二套 IPC 装配入口，"
            + "删除属跨二进制破坏性变更，跟 SDK 版本号一起定：待办 G1-U",
        ["ServiceCollectionExtensions.AddPublicIpcService"] = "同上，另一套 IPC 注册入口：待办 G1-U",
        ["LauncherRuntimeMetadata.GetLauncherProcessId"] = "启动器两侧都写了 LMD_LAUNCHER_PID（HostLaunchPlan 的 env + --参数），"
            + "宿主侧读取为零：单侧契约，要不要拿它做父进程监视是产品/架构决定",
        ["LauncherRuntimeMetadata.GetRestartParentProcessId"] = "重启时 --restart-parent-pid 由 AppRestartService 写入，读取为零："
            + "同上，属写了一半的跨进程契约",
    };

    private static readonly string[] HostDirectories =
        ["desktop/LanMountainDesktop", "desktop/LanMountainDesktop.Launcher", "core", "install", "platform", "packaging", "scripts"];

    private static readonly string[] CorpusExtensions =
        [".cs", ".axaml", ".xaml", ".json", ".md", ".props", ".csproj", ".yml", ".yaml", ".iss", ".ps1"];

    private static readonly string[] SkipDirectoryNames =
        ["bin", "obj", ".git", ".vs", ".idea", "node_modules", "artifacts"];

    private static readonly Regex StaticClass = new(
        @"\bstatic\s+(?:sealed\s+)?(?:partial\s+)?class\s+(?<cls>[A-Za-z_]\w*)",
        RegexOptions.Compiled);

    private static readonly Regex StaticMethod = new(
        @"^[ \t]*(?:public|internal)[ \t]+(?:[^=\r\n]*?[ \t])?static[ \t]+(?:[^=\r\n]*?[ \t])?(?<name>[A-Za-z_]\w*)[ \t]*(?:<[^>\r\n]*>)?[ \t]*\(",
        RegexOptions.Compiled);

    private static readonly Regex Identifier = new("[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    private static readonly HashSet<string> IgnoredNames =
        ["Main", "operator", "implicit", "explicit", "Equals", "GetHashCode", "ToString", "GetType"];

    [Fact]
    public void ZeroUseStaticClassMembers_MatchTheAcceptedList()
    {
        var repoRoot = RepoRoot();
        var declared = new List<(string Key, string File, string Line)>();
        var hostFiles = EnumerateFiles(repoRoot, HostDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var file in hostFiles)
        {
            var relative = Relative(repoRoot, file);
            var source = File.ReadAllText(file);
            var lines = source.Split('\n');
            var classSpans = StaticClass.Matches(source)
                .Select(m => (Index: m.Index, ClassName: m.Groups["cls"].Value))
                .ToList();
            var offset = 0;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                var match = StaticMethod.Match(line);
                if (match.Success &&
                    !line.Contains("partial ", StringComparison.Ordinal) &&
                    !match.Groups["name"].Value.StartsWith("op_", StringComparison.Ordinal) &&
                    !IgnoredNames.Contains(match.Groups["name"].Value)
                    // `*ForTests` 是约定的"只给测试用的重置入口"（如 AppDataPathProvider.ResetForTests）：
                    // 生产不该调它，把语料收窄到产品可达之后就别再把它当死码报出来。
                    && !match.Groups["name"].Value.EndsWith("ForTests", StringComparison.Ordinal))
                {
                    var owner = classSpans.Where(span => span.Index < offset).LastOrDefault();
                    if (owner.ClassName is not null)
                    {
                        declared.Add(($"{owner.ClassName}.{match.Groups["name"].Value}", relative, line.Trim()));
                    }
                }

                offset += rawLine.Length + 1;
            }
        }

        // 与 ZeroUseTypeRatchetTests 同口径：只认生产目录的真调用（docs 里一份历史计划文档
        // 抄过 `GetScreenInfo()` 的代码块，曾把它算成"有人用"）。
        var corpusDirectories = HostDirectories
            .Concat(["core", "airapp", "install", "mobile", "platform", "packaging", "scripts",
                "tests/LanMountainDesktop.Tests/ApprovalFiles"]);
        var corpus = EnumerateFiles(repoRoot, corpusDirectories)
            .Where(path => CorpusExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            // 名单文件自己不算使用者：Accepted 里写的就是这些成员名，否则每条登记都自证"有人用"。
            .Where(path => !path.EndsWith("RatchetTests.cs", StringComparison.OrdinalIgnoreCase))
            .Select(path => (Relative: Relative(repoRoot, path), Lines: File.ReadAllLines(path)))
            .ToList();

        var unexpected = new List<string>();
        var noLongerZeroUse = new List<string>();
        var foundKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, file, _) in declared)
        {
            foundKeys.Add(key);
            var name = key.Split('.')[1];
            var references = 0;

            foreach (var (relative, lines) in corpus)
            {
                var isDeclaringFile = string.Equals(relative, file, StringComparison.Ordinal);
                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var hits = Regex.Matches(line, $@"(?<![A-Za-z0-9_]){name}(?![A-Za-z0-9_])").Count;
                    if (hits == 0)
                    {
                        continue;
                    }

                    if (isDeclaringFile && StaticMethod.IsMatch(rawLine))
                    {
                        continue;
                    }

                    references += hits;
                }
            }

            if (references == 0 && !Accepted.ContainsKey(key))
            {
                unexpected.Add(key);
            }

            if (references > 0 && Accepted.ContainsKey(key))
            {
                noLongerZeroUse.Add(key);
            }
        }

        var stale = Accepted.Keys.Where(key => !foundKeys.Contains(key)).ToList();

        // 反向：登记项若已经被人真引用了，它就是假欠账——留着会让下次复查以为"这条已经查过"。
        Assert.True(
            unexpected.Count == 0 && stale.Count == 0 && noLongerZeroUse.Count == 0,
            $"新增零引用静态成员 {unexpected.Count} 个：{string.Join(", ", unexpected.Order(StringComparer.Ordinal))}" +
            $"{Environment.NewLine}名单里已不存在的条目 {stale.Count} 个（删掉方法后请把条目一起删）：{string.Join(", ", stale.Order(StringComparer.Ordinal))}" +
            $"{Environment.NewLine}名单里已成假欠账的条目 {noLongerZeroUse.Count} 个（现在有人引用了，请删掉这条登记）：{string.Join(", ", noLongerZeroUse.Order(StringComparer.Ordinal))}");
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
