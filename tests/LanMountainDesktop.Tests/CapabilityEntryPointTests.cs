using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// "有实现、没入口"守卫。设置页/组件的可达性另有测试钉住（`DesktopComponentReachabilityTests`、
/// 设置页导航只列非 HideDefault 的页），这里补两条代码上能量出来的：
///
/// 1) 每个 <c>[RelayCommand]</c> 生成的命令都必须被某处绑到（.axaml 的 Command 绑定、代码里的引用都算）。
///    绑不上的命令就是"写了个用户点不到的动作"。
/// 2) 每个 <c>Button</c> / <c>ToggleButton</c> 要么有 Command/Click/名字，要么带 Flyout——
///    否则它看得见、点下去什么都不发生，是最典型的一类宿主 UI 缺陷。
///
/// 前两条今天都是 0 命中，所以不留名单：新增即红。第三条只留了两条写明理由的名单项
/// （一条"进程级唯一实例不必解订"、一条"发了没人接"的待决能力），其余新增即红。
/// </summary>
public sealed class CapabilityEntryPointTests
{
    private static readonly string[] SkipDirectoryNames =
        ["bin", "obj", ".git", ".vs", ".idea", "node_modules", "artifacts"];

    private static readonly Regex RelayCommandAttribute = new(@"\[RelayCommand", RegexOptions.Compiled);

    private static readonly Regex MethodAfterAttribute = new(
        @"^\s*(?:public|internal|private)[^\r\n]*?\s(?<name>[A-Za-z_]\w*)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex ButtonOpenTag = new(@"<(Button|ToggleButton|RepeatButton)([\s>][^<]*?)(/?)>", RegexOptions.Compiled);

    [Fact]
    public void EveryRelayCommand_IsBoundSomewhere()
    {
        var repoRoot = RepoRoot();
        var corpus = LoadCorpus(repoRoot);
        var offenders = new List<string>();

        foreach (var (relative, text) in corpus.Where(entry => entry.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                     && entry.Key.StartsWith("desktop/LanMountainDesktop/", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (!RelayCommandAttribute.IsMatch(lines[index]))
                {
                    continue;
                }

                var commandName = ResolveCommandName(lines, index);
                if (commandName is null)
                {
                    continue;
                }

                var references = 0;
                var lookup = new Regex($@"(?<![A-Za-z0-9_]){commandName}(?![A-Za-z0-9_])");
                foreach (var (otherPath, otherText) in corpus)
                {
                    if (string.Equals(otherPath, relative, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    references += lookup.Matches(otherText).Count;
                }

                if (references == 0)
                {
                    offenders.Add($"{relative}:{index + 1} 生成的 {commandName} 全仓没有任何绑定或引用");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 条有实现没入口的命令：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void EveryButton_CanActuallyBeTriggered()
    {
        var repoRoot = RepoRoot();
        var offenders = new List<string>();

        foreach (var (relative, text) in LoadCorpus(repoRoot)
                     .Where(entry => entry.Key.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                         && entry.Key.StartsWith("desktop/", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (Match match in ButtonOpenTag.Matches(text))
            {
                var tag = match.Groups[2].Value;
                // (/?) 就算没匹配到斜杠也是 Success（捕获成空串），只能比值。
                var isSelfClosing = match.Groups[3].Value.Length > 0;
                var body = isSelfClosing ? string.Empty : ReadElementBody(text, match.Index, match.Groups[1].Value);

                if (Regex.IsMatch(tag, @"(Command|Click|x:Name|Name|CommandParameter|EventName)=") ||
                    Regex.IsMatch(tag + body, @"\.Flyout|PointerPressed|Tapped|Button\.Command"))
                {
                    continue;
                }

                var line = text[..match.Index].Split('\n').Length;
                offenders.Add($"{relative}:{line} 这个按钮既没绑命令也没 Flyout：{tag.Trim()[..Math.Min(60, tag.Trim().Length)]}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 个看得见点不动的按钮：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// 宿主里的 <c>static event</c>：既得有人订、也得有人发，订的一方还得会解订
    /// （静态事件会把订阅者永久根住，页面级对象订了就等于每开一次泄漏一个）。
    ///
    /// 配对按"声明类型 + 事件名"而不是只看名字：宿主里有 3 个静态事件、也有同名的实例事件
    /// （<c>_desktopTrayService.StateChanged</c>），只按名字量会把实例订阅算成静态订阅，
    /// 也会把 <c>AppSettingsService/LauncherSettingsService</c> 两份同名 <c>SettingsSaved</c> 混成一条。
    /// </summary>
    [Fact]
    public void StaticEvents_HavePublisherSubscriberAndRelease()
    {
        // 订阅方是进程级唯一实例时"不解订"是对的，但必须写清是谁保证了唯一。
        var permanentSubscriptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AppSettingsService.SettingsSaved <- desktop/LanMountainDesktop/Services/Settings/SettingsWindowService.cs"] =
                "SettingsWindowService 由 App 懒建一次（App.axaml.cs:793 的 ??=），生命周期等于进程，解订反而是空转",
        };

        // 发了没人接的事件：删它要连累写盘路径，属"有实现、没入口"，等用户拍板（见 backlog 记忆）。
        var unwiredEvents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LauncherSettingsService.SettingsSaved"] =
                "AppSettingsService 的复制体：Save 后 Invoke，但全仓零订阅者（连 AppSettingsService 那条的唯一订阅者也不订它）",
        };

        var repoRoot = RepoRoot();
        var host = LoadCorpus(repoRoot)
            .Where(entry => entry.Key.StartsWith("desktop/LanMountainDesktop/", StringComparison.OrdinalIgnoreCase)
                && entry.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var offenders = new List<string>();
        var usedUnwiredEvents = new List<string>();
        var usedPermanentSubscriptions = new List<string>();

        foreach (var (file, text) in host)
        {
            foreach (Match declaration in Regex.Matches(text, @"static event [^;=\r\n]+ (?<name>[A-Za-z_]\w*)\s*;"))
            {
                var name = declaration.Groups["name"].Value;
                var id = $"{DeclaringType(text, declaration.Index)}.{name}";
                var line = LineOf(text, declaration.Index);

                var publishers = host.Count(entry =>
                    EventAccess(id, file, entry.Key, @"\?\.\s*Invoke").IsMatch(entry.Value));
                var subscriberFiles = host
                    .Where(entry => EventAccess(id, file, entry.Key, @"\+=").IsMatch(entry.Value))
                    .Select(entry => entry.Key)
                    .ToList();

                if (publishers == 0)
                {
                    offenders.Add($"{file}:{line} {id} 这个静态事件没人发");
                }

                if (subscriberFiles.Count == 0)
                {
                    if (!unwiredEvents.ContainsKey(id))
                    {
                        offenders.Add($"{file}:{line} {id} 这个静态事件全仓没人订（新增即红；确属待决能力就写进本测试的 unwiredEvents 并附理由）");
                    }
                    else
                    {
                        usedUnwiredEvents.Add(id);
                    }

                    continue;
                }

                foreach (var subscriber in subscriberFiles)
                {
                    var key = $"{id} <- {subscriber}";
                    if (permanentSubscriptions.ContainsKey(key))
                    {
                        if (EventAccess(id, file, subscriber, @"\-=").IsMatch(host[subscriber]))
                        {
                            offenders.Add($"名单项 {key} 已过期：{subscriber} 已经会解订了");
                        }
                        else
                        {
                            usedPermanentSubscriptions.Add(key);
                        }

                        continue;
                    }

                    if (!EventAccess(id, file, subscriber, @"\-=").IsMatch(host[subscriber]))
                    {
                        offenders.Add($"{subscriber} 订了静态事件 {id} 却不解订：单例会永久攥住这个对象");
                    }
                }
            }
        }

        foreach (var stale in unwiredEvents.Keys.Where(entry => !usedUnwiredEvents.Contains(entry)))
        {
            offenders.Add($"名单项 {stale} 已过期：{stale} 已经不存在，或已经有人订了");
        }

        foreach (var stale in permanentSubscriptions.Keys.Where(entry => !usedPermanentSubscriptions.Contains(entry)))
        {
            offenders.Add($"名单项 {stale} 已过期：那个订阅者已经不订了");
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处静态事件问题：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static int LineOf(string text, int index) => text[..index].Split('\n').Length;

    /// <summary>取声明这条事件的那个类型（就近向前找最近的类型声明）。</summary>
    private static string DeclaringType(string text, int declarationIndex)
    {
        var types = Regex.Matches(text[..declarationIndex], @"\b(?:class|record|struct|interface)\s+(?<type>[A-Za-z_]\w*)");
        return types.Count > 0 ? types[^1].Groups["type"].Value : "?";
    }

    /// <summary>
    /// 静态成员在声明类型之外必须写成 <c>Type.Event</c>；在声明它的那个类型里可以裸写。
    /// 一个文件常装两个类型，所以"同文件"只多认一种裸写形态，限定形态始终要认。
    /// </summary>
    private static Regex EventAccess(string id, string declaringFile, string currentFile, string operation)
    {
        var dot = id.IndexOf('.', StringComparison.Ordinal);
        var (declaringType, name) = (id[..dot], id[(dot + 1)..]);
        var qualified = $@"(?<!\w){Regex.Escape(declaringType)}\s*\.\s*{Regex.Escape(name)}\s*{operation}";
        if (!string.Equals(declaringFile, currentFile, StringComparison.Ordinal))
        {
            return new Regex(qualified, RegexOptions.Compiled);
        }

        return new Regex(
            $@"{qualified}|(?<![\w.]){Regex.Escape(name)}\s*{operation}",
            RegexOptions.Compiled);
    }

    private static string? ResolveCommandName(string[] lines, int attributeIndex)
    {
        for (var probe = attributeIndex + 1; probe < Math.Min(attributeIndex + 8, lines.Length); probe++)
        {
            var match = MethodAfterAttribute.Match(lines[probe]);
            if (!match.Success)
            {
                continue;
            }

            var method = match.Groups["name"].Value;
            if (method.EndsWith("Async", StringComparison.Ordinal))
            {
                method = method[..^5];
            }

            return $"{method}Command";
        }

        return null;
    }

    /// <summary>取元素自身的那段内容（含嵌套同名标签时按深度配平），用于判断它有没有挂 Flyout。</summary>
    private static string ReadElementBody(string text, int openIndex, string tagName)
    {
        var depth = 0;
        var cursor = openIndex;
        while (cursor < text.Length)
        {
            var nextOpen = text.IndexOf($"<{tagName}", cursor, StringComparison.Ordinal);
            var nextClose = text.IndexOf($"</{tagName}>", cursor, StringComparison.Ordinal);
            if (nextClose < 0)
            {
                break;
            }

            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth++;
                cursor = nextOpen + tagName.Length + 2;
                continue;
            }

            if (depth == 0)
            {
                return text[openIndex..(nextClose + tagName.Length + 3)];
            }

            depth--;
            cursor = nextClose + tagName.Length + 3;
        }

        return text[openIndex..];
    }

    private static Dictionary<string, string> LoadCorpus(string repoRoot)
    {
        var corpus = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(repoRoot, "*", SearchOption.AllDirectories))
        {
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(repoRoot, path)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (relative.Split(Path.DirectorySeparatorChar)
                    .Any(part => SkipDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                corpus[relative.Replace('\\', '/')] = File.ReadAllText(path);
            }
            catch (IOException)
            {
                // 读不动的文件不算引用，跳过。
            }
        }

        return corpus;
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

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
