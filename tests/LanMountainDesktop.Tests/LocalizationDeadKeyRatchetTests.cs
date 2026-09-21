using System.Text.Json;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 本地化键的"死没死"要按模板能不能拼出来判，否则量出来的数不能信。
///
/// 键进本地化的写法不止 <c>L("a.b")</c> 一种：
/// <list type="bullet">
///   <item>内插：<c>L($"settings.appearance.corner_radius.style_{style.ToShort()}")</c></item>
///   <item>拼接：<c>L("settings.nav." + pageId)</c>（字面量以 <c>.</c> 或 <c>_</c> 结尾）</item>
///   <item>变量：<c>L(section.TitleLocalizationKey)</c>，值在别处用字面量赋</item>
///   <item>藏在洞里的调用：<c>$"{L("rss.refresh_failed", "…")}: {ex.Message}"</c></item>
/// </list>
/// 只按"整串字面量在源码里出现过没有"判会高估死键（同一棵树实测 404 / 415 / 559 三个数，
/// 全取决于漏看了哪一类写法；漏看最后一条时实测误删了 3 条活键），所以这几类都算可达，并把语料固定为
/// "会被编进产物的目录 + 仓内所有 json"，不含测试代码（测试里的键名不算产品用到）。
/// 两个方向共用同一套字面量扫描：一头判死、另一头判活，就会删完才红。
///
/// 反方向（代码要的键 json 里没有）只认 <c>L</c> / <c>Lf</c> / <c>GetString</c> 的实参位置，
/// 否则 "index.json"、"github.com" 这类带点字面量会全被判成缺键（实测 302 条里大部分是这种噪声）。
/// </summary>
public sealed class LocalizationDeadKeyRatchetTests
{
    /// <summary>
    /// 2026-09-21 把 <see cref="AcceptedKeysMissingFromZhCn"/> 一族的漂移修完后，按上面三种写法
    /// 量出 357 条拼不出来的键并全部删除（<c>settings.update.*</c> 一组占大头，是更新页改版留下的旧文案）。
    /// 保持 0：新增键就得有地方用到它，否则先删；真要留就把数字抬上去并写清理由。
    /// 用 LMD_EMIT_DEAD_KEYS=1 重跑可以拿到当前清单。
    /// </summary>
    private const int AcceptedUnreferencedKeyCount = 0;

    /// <summary>
    /// 代码引用、zh-CN.json 里没有的键数（只降不升）。2026-09-21 量出 25 个并已全部清零：
    /// 全是组件编辑器/设置页面板的键漂移（代码写 <c>baidu.settings.desc</c>，词表里是
    /// <c>baiduhot.settings.desc</c>；<c>component.editor.{desc,toggle,interval}</c> 三条是词表根本没有，
    /// 已按代码兜底文案补进四份词表）。症状原本是这些面板在中文界面显示硬编码英文。
    /// 保持 0：再出现就是新漂移，改代码键名而不是抬这个数字。
    /// </summary>
    private const int AcceptedKeysMissingFromZhCn = 0;

    private static readonly string[] SourceRoots = ["desktop", "airapp", "install", "platform", "core"];

    private static readonly string[] Locales = ["zh-CN", "en-US", "ja-JP", "ko-KR"];

    private static readonly string[] SkipDirectoryNames =
        ["bin", "obj", ".git", ".vs", ".idea", "node_modules", "artifacts"];

    [Fact]
    public void UnreferencedLocalizationKeys_DoNotGrow()
    {
        var repoRoot = RepoRoot();
        var keys = AllLocaleKeys(repoRoot);

        var literals = ProductLiterals(repoRoot);
        var templates = InterpolatedTemplates(repoRoot);
        var prefixes = literals.Where(text => EndsAtSeparator(text) && text.Length >= 8).ToArray();
        var exact = new HashSet<string>(literals, StringComparer.Ordinal);

        var dead = keys
            .Where(key => !exact.Contains(key)
                && !prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))
                && !templates.Any(pattern => pattern.IsMatch(key)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        // 删键用的清单由这条测试自己产（判据只有一份），照 SDK 基线的老规矩用环境变量触发。
        // 放在断言之前：超基线时也要能拿到清单，否则红着删不动。
        if (Environment.GetEnvironmentVariable("LMD_EMIT_DEAD_KEYS") is "1" or "true")
        {
            var target = Path.Combine(
                repoRoot, "tests", "LanMountainDesktop.Tests", "ApprovalFiles", "Localization.DeadKeys.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllLines(target, dead);
        }

        Assert.True(
            dead.Length <= AcceptedUnreferencedKeyCount,
            $"{dead.Length} 个本地化键在产品里拼不出来（基线 {AcceptedUnreferencedKeyCount}）。"
            + $"按顶层分组：{string.Join(", ", dead.GroupBy(k => k.Split('.')[0]).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={g.Count()}"))}"
            + $"前 40 个：{Environment.NewLine}{string.Join(Environment.NewLine, dead.Take(40))}");

        Assert.True(
            dead.Length >= AcceptedUnreferencedKeyCount,
            $"只剩 {dead.Length} 个未引用键，比基线 {AcceptedUnreferencedKeyCount} 少："
            + $"把 AcceptedUnreferencedKeyCount 改成 {dead.Length}（删完一批键后，"
            + "用 LMD_EMIT_DEAD_KEYS=1 重跑这条测试可以拿到当前清单）。");
    }

    /// <summary>
    /// 键名赋给本地化用的字段/属性（<c>DescriptionKey = "baidu.settings.desc"</c>）后由别处
    /// <c>L(variable)</c> 消费，所以只扫 <c>L(</c> 的实参会漏掉整条编辑器面板的键。
    /// 名单按命名约定收：<c>*LocalizationKey</c> 结尾，或编辑器选项里那几个 *Key；
    /// <c>PublicKey = "public-key.pem"</c>、<c>...MetaKey = "study.selected_session_report_id"</c>
    /// 这类存储/加密键不算（实测会被误判成缺键）。
    /// </summary>
    private static readonly Regex KeyAssignment = new(
        @"\b(?<name>[A-Za-z0-9_]*(?:LocalizationKey|TitleKey|DescriptionKey|LabelKey|ToggleLabelKey|HeaderKey|TextKey|NameKey))\s*=\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void EveryKeyRequestedFromCode_ExistsInZhCn()
    {
        var repoRoot = RepoRoot();
        var keys = new HashSet<string>(
            ReadKeys(Path.Combine(repoRoot, "desktop", "LanMountainDesktop", "Localization", "zh-CN.json")),
            StringComparer.Ordinal);

        var requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in ProductSourceFiles(repoRoot))
        {
            var source = File.ReadAllText(file);

            foreach (var argument in KeyArguments([file]))
            {
                foreach (var literal in StringLiterals(argument))
                {
                    // 只认整串键；"refresh.frequency." + x 这种拼接前缀留给死键方向去判。
                    if (literal.Holes == 0 && LooksLikeKey(literal.Text) && !EndsAtSeparator(literal.Text))
                    {
                        requested.Add(literal.Text);
                    }
                }
            }

            foreach (Match match in KeyAssignment.Matches(source))
            {
                if (LooksLikeKey(match.Groups["key"].Value))
                {
                    requested.Add(match.Groups["key"].Value);
                }
            }
        }

        var missing = requested.Where(key => !keys.Contains(key)).OrderBy(key => key, StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length <= AcceptedKeysMissingFromZhCn,
            $"{missing.Length} 个代码引用的键在 zh-CN.json 里没有（基线 {AcceptedKeysMissingFromZhCn}）——"
            + $"运行时落到硬编码兜底文案，中文界面于是混进英文："
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, missing.Take(40))}");
    }

    /// <summary>
    /// 死键判据覆盖四份词表的键并集，不只 zh-CN：只在 ja/ko 里多出来的键（实测 2 个
    /// <c>settings.wallpaper.system.*</c>）用 zh 当分母时永远看不见，同样是拼不出来的死文案。
    /// </summary>
    private static List<string> AllLocaleKeys(string repoRoot) =>
        Locales
            .Select(locale => Path.Combine(
                repoRoot, "desktop", "LanMountainDesktop", "Localization", $"{locale}.json"))
            .Where(File.Exists)
            .SelectMany(ReadKeys)
            .ToList();

    private static IEnumerable<string> ProductLiterals(string repoRoot) =>
        ProductSourceFiles(repoRoot)
            .Select(path => File.ReadAllText(path))
            .SelectMany(text => StringLiterals(text))
            .Select(literal => literal.Text)
            .Concat(JsonStrings(repoRoot))
            .ToArray();

    private static IEnumerable<Regex> InterpolatedTemplates(string repoRoot) =>
        ProductSourceFiles(repoRoot)
            .Select(path => File.ReadAllText(path))
            .SelectMany(text => StringLiterals(text))
            .Where(literal => literal.Holes > 0 && literal.Parts[0].Contains('.'))
            .Select(BuildPattern)
            .Where(pattern => pattern is not null)
            .Select(pattern => pattern!)
            .ToArray();

    private static Regex? BuildPattern(StringLiteral literal)
    {
        if (!LooksLikeKey(Raw(literal.Parts[0])))
        {
            return null;
        }

        var builder = new System.Text.StringBuilder('^');
        for (var index = 0; index < literal.Parts.Count; index++)
        {
            builder.Append(Regex.Escape(Raw(literal.Parts[index])));
            if (index < literal.Holes)
            {
                builder.Append("[A-Za-z0-9_\\-]+");
            }
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);

        static string Raw(string text) => text;
    }

    /// <summary>取 L("…") / Lf("…") / GetString(lang, "…", …) 的键实参文本。</summary>
    private static IEnumerable<string> KeyArguments(IEnumerable<string> files)
    {
        foreach (var source in files.Select(File.ReadAllText))
        {
            foreach (Match match in Regex.Matches(source, @"\b(?<call>Lf?|GetString)\s*\("))
            {
                var open = match.Index + match.Length;
                var args = SplitArguments(source, open);
                var index = match.Groups["call"].Value == "GetString" ? 1 : 0;
                if (args.Count > index)
                {
                    yield return args[index];
                }
            }
        }
    }

    private static List<string> SplitArguments(string source, int openIndex)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;
        var inString = false;
        var stringQuote = '"';

        for (var cursor = openIndex; cursor < source.Length; cursor++)
        {
            var c = source[cursor];

            if (inString)
            {
                current.Append(c);
                if (c == '\\' && cursor + 1 < source.Length)
                {
                    current.Append(source[++cursor]);
                }
                else if (c == stringQuote)
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    stringQuote = '"';
                    current.Append(c);
                    break;
                case '(' when depth == 0:
                    depth++;
                    current.Append(c);
                    break;
                case '(':
                case '[':
                case '{':
                    depth++;
                    current.Append(c);
                    break;
                case ')' when depth == 0:
                    args.Add(current.ToString());
                    return args;
                case ')' or ']' or '}':
                    depth--;
                    current.Append(c);
                    break;
                case ',' when depth == 0:
                    args.Add(current.ToString());
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        return args;
    }

    private static IEnumerable<string> JsonStrings(string repoRoot) =>
        SourceRoots
            .Select(part => Path.Combine(repoRoot, part))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => SkipDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase)))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Localization{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file =>
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    return EnumerateJsonStrings(document.RootElement).ToArray();
                }
                catch (JsonException)
                {
                    return Array.Empty<string>();
                }
                catch (IOException)
                {
                    return Array.Empty<string>();
                }
            });

    private static IEnumerable<string> EnumerateJsonStrings(JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                {
                    foreach (var nested in EnumerateJsonStrings(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    foreach (var nested in EnumerateJsonStrings(item))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.String when node.GetString() is { } text:
                yield return text;
                break;
        }
    }

    private static IEnumerable<string> ProductSourceFiles(string repoRoot) =>
        SourceRoots
            .Select(part => Path.Combine(repoRoot, part))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(directory, "*.axaml", SearchOption.AllDirectories)))
            .Where(file => !file.Split(Path.DirectorySeparatorChar)
                .Any(part => SkipDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase)));

    private static List<string> ReadKeys(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var keys = new List<string>();
        Walk(document.RootElement, string.Empty, keys);
        return keys;
    }

    private static void Walk(JsonElement node, string prefix, List<string> keys)
    {
        foreach (var property in node.EnumerateObject())
        {
            var key = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                Walk(property.Value, key, keys);
            }
            else
            {
                keys.Add(key);
            }
        }
    }

    private static bool LooksLikeKey(string text) =>
        text.Contains('.') &&
        text.Length >= 5 &&
        text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') &&
        char.IsAsciiLetterLower(text[0]);

    private static bool EndsAtSeparator(string text) => text.Length > 0 && text[^1] is '.' or '_';

    /// <summary>一个字符串字面量：Parts 是洞之间的文本，洞数 = Parts.Count - 1。</summary>
    private sealed record StringLiteral(List<string> Parts)
    {
        public int Holes => Parts.Count - 1;

        public string Text => string.Concat(Parts);
    }

    /// <summary>
    /// 扫 regular / interpolated / verbatim 三种字符串；跳过 raw string（键不会写成那个）。
    /// 内插洞里嵌的代码同样要扫：<c>$"{L("rss.refresh_failed", "…")}: {ex.Message}"</c> 里那条键
    /// 只在洞里出现，不递归就量不到——实测因此把 3 条活键判成死的并删了。
    /// </summary>
    private static IEnumerable<StringLiteral> StringLiterals(string source)
    {
        for (var index = 0; index < source.Length; index++)
        {
            var first = source[index];
            if (first is not ('"' or '$' or '@'))
            {
                continue;
            }

            var interpolated = false;
            var verbatim = false;
            var quoteIndex = index;

            if (first == '$' || first == '@')
            {
                if (index + 1 >= source.Length || source[index + 1] != '"')
                {
                    continue;
                }

                interpolated = first == '$';
                verbatim = first == '@';
                quoteIndex = index + 1;
            }
            else if (index > 0 && (source[index - 1] == '$' || source[index - 1] == '@'))
            {
                interpolated = source[index - 1] == '$';
                verbatim = source[index - 1] == '@';
            }

            var parts = new List<string>();
            var nested = new List<StringLiteral>();
            var current = new System.Text.StringBuilder();
            var holes = 0;
            var closed = false;

            for (var cursor = quoteIndex + 1; cursor < source.Length; cursor++)
            {
                var c = source[cursor];

                if (!verbatim && c == '\\')
                {
                    if (cursor + 1 < source.Length)
                    {
                        current.Append(source[++cursor]);
                        continue;
                    }

                    continue;
                }

                if (verbatim && c == '"' && cursor + 1 < source.Length && source[cursor + 1] == '"')
                {
                    current.Append('"');
                    cursor++;
                    continue;
                }

                if (c == '"')
                {
                    parts.Add(current.ToString());
                    index = cursor;
                    closed = true;
                    break;
                }

                if (interpolated && c == '{')
                {
                    if (cursor + 1 < source.Length && source[cursor + 1] == '{')
                    {
                        current.Append('{');
                        cursor++;
                        continue;
                    }

                    parts.Add(current.ToString());
                    current.Clear();
                    holes++;
                    var holeEnd = SkipInterpolation(source, cursor);
                    nested.AddRange(StringLiterals(source[(cursor + 1)..holeEnd]));
                    cursor = holeEnd;
                    continue;
                }

                if (interpolated && c == '}')
                {
                    if (cursor + 1 < source.Length && source[cursor + 1] == '}')
                    {
                        current.Append('}');
                        cursor++;
                        continue;
                    }

                    cursor = SkipInterpolation(source, cursor);
                    continue;
                }

                current.Append(c);
            }

            if (closed && parts.Count == holes + 1)
            {
                yield return new StringLiteral(parts);
                foreach (var inner in nested)
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>从 '{'（或多余的 '}'）走到配平的右花括号。</summary>
    private static int SkipInterpolation(string source, int braceIndex)
    {
        var depth = 0;
        for (var cursor = braceIndex; cursor < source.Length; cursor++)
        {
            if (source[cursor] == '{')
            {
                depth++;
            }
            else if (source[cursor] == '}' && --depth == 0)
            {
                return cursor;
            }
        }

        return source.Length;
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

        throw new InvalidOperationException("找不到仓库根。");
    }
}
