using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件自己那套"把尺寸/主题换成样式"的方法，必须真的有人调用。
///
/// 为什么值得钉成闸门（症状不是崩，是不对）：这类方法定义了却没人调时，组件会按 XAML 默认样式画出来，
/// 缩放与排版应用不上，运行期没有任何异常或日志会提示。2026-09-22 用脚本量过一次是 0 处未调用，
/// 但脚本不在闸门里，下一次新增组件抄漏一行不会有人发现。
///
/// 判据：在组件目录里定义这些方法、且本文件与全仓都找不到调用点，就是缺陷。
/// 一条关键豁免：<c>override</c> / <c>abstract</c> 不算——它们由基类或接口派发
/// （<c>WeatherWidgetBase.cs:86</c> 会在基类里调子类的 <c>ApplyResponsiveLayout</c>）。
/// 第一版没做这条豁免时，5 个天气组件全被报成缺陷，全是假阳性（实测）。
///
/// 覆盖面也钉住：名字表里每个方法名都必须在磁盘上至少出现一次。
/// 删掉一整族方法、或者把名字表改窄，都会让"未调用 0 处"照样成立——那种静默收窄比红灯贵。
///
/// 一条已知漏报方向（保守，不是误报）：跨文件的调用点是按**名字**认的（<c>.Name(</c> 形态，
/// 本文件那一侧放宽到"名字作为一个完整标识符出现"，好让方法组实参也算，见 <see cref="Invocation"/>），
/// 因为这一族里 <c>ApplyCellSize</c> 实测有 22 处是运行期注册表按实例统一推的，
/// 逐个文件去配对会把"由别人调"的真调用判成缺失。代价是：同名成员只要在任意一处有 <c>.Name(</c>，
/// 别的文件里没人调的定义也会被算成已调用。
/// </summary>
public sealed class WidgetLayoutAppliedRatchetTests
{
    /// <summary>组件里"把尺寸/主题换成样式"这一族方法名。</summary>
    private static readonly string[] LayoutMethodNames =
    [
        "ApplyCellSize",
        "ApplyResponsiveLayout",
        "UpdateAdaptiveLayout",
        "ApplyAdaptiveLayout",
        "ApplyLayoutMetrics",
        "ApplyTypographyByBackground",
        "ApplyChrome",
    ];

    private static readonly string[] ComponentDirectories =
    [
        Path.Combine("desktop", "LanMountainDesktop", "Views", "Components"),
        Path.Combine("desktop", "LanMountainDesktop", "ComponentSystem"),
    ];

    /// <summary>全仓扫调用点的范围：组件之外也可能按具体类型统一推（如运行期注册表）。</summary>
    private static readonly string[] ScanDirectories =
        ["core", "desktop", "airapp", "install", "platform", "mobile"];

    private static readonly Regex Definition = new(
        @"\b(?:public|private|protected|internal)[\w ]*?\b(?<name>" + string.Join('|', LayoutMethodNames) + @")\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex Dispatched = new(@"\b(?:override|abstract)\b", RegexOptions.Compiled);

    /// <summary>
    /// 调用点：同一个名字作为一个完整的标识符出现（后面不接标识符字符），且前面不是标识符字符
    /// （避开 <c>Foo.ApplyCellSizeGroup</c>）。
    /// <b>为什么不只认 <c>.Name(</c></b>：2026-09-25 把 5 个学习组件的"尺寸变了重排 + 按底色重算"
    /// 收进 <c>StudyComponentLifecycle.RefreshOnResize</c> 之后，这条闸门一次报出 5 处"没人调"——
    /// 那 5 个组件此刻写的是 <c>RefreshOnResize(RootBorder, UpdateAdaptiveLayout, ApplyTypographyByBackground)</c>，
    /// <b>方法组当实参递出去</b>就是调用点（由那个家去调），带括号的形态反而是唯一被排除的声明行。
    /// 放宽的代价：把名字写进字符串或文档注释也算"有人引用"，但那不是这一族会漂开的方式
    /// （实际漂开方式是抄本漏掉那一步，见本条闸门的由来）。
    /// </summary>
    private static readonly Regex Invocation = new(
        @"(?<![A-Za-z0-9_])(?<name>" + string.Join('|', LayoutMethodNames) + @")(?![A-Za-z0-9_])",
        RegexOptions.Compiled);

    [Fact]
    public void EveryLayoutMethodDefinition_HasACallSite()
    {
        var repoRoot = RepoRoot();
        var definitions = new List<(string File, int Line, string Name, bool Dispatched)>();
        var calledInOwnFile = new HashSet<(string, string)>();

        foreach (var path in EnumerateCSharpFiles(repoRoot, ComponentDirectories))
        {
            var relative = Relative(repoRoot, path);
            foreach (var (line, number) in ReadLines(path))
            {
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                var declarationSpans = Definition.Matches(line)
                    .Select(match => (match.Groups["name"].Value,
                                      Index: match.Groups["name"].Index,
                                      End: match.Groups["name"].Index + match.Groups["name"].Length))
                    .ToList();

                foreach (var (name, index, _) in declarationSpans)
                {
                    definitions.Add((relative, number, name, Dispatched.IsMatch(line)));
                }

                // 调用点扫描与声明无关：纯调用行（组件在 Attach/布局回调里调自己那套方法）也要记下来。
                // 早版把这段跟在"本行有声明"后面 continue，同文件的真调用全被吞掉，一次报出整片假阳性
                // （第一条撞上的是 BaiduHotSearchWidget.axaml.cs:326 的 UpdateAdaptiveLayout）。
                foreach (Match match in Invocation.Matches(line))
                {
                    if (!declarationSpans.Any(span => match.Index >= span.Index && match.Index < span.End))
                    {
                        calledInOwnFile.Add((relative, match.Groups["name"].Value));
                    }
                }
            }
        }

        var externalCalls = CountExternalInvocations(repoRoot);

        var offenders = definitions
            .Where(item => !item.Dispatched &&
                           !calledInOwnFile.Contains((item.File, item.Name)) &&
                           !externalCalls.Contains(item.Name))
            .ToList();

        Assert.NotEmpty(definitions);
        Assert.True(
            offenders.Count == 0,
            $"这些布局/样式应用方法定义了却没人调（组件会按 XAML 默认样式画出来、缩放不生效，且不会报错）：" +
            string.Join(" | ", offenders.Select(item => $"{item.File}:{item.Line} {item.Name}()")
                .Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// 名字表不许悄悄变窄：每个方法名都要在磁盘上至少出现一次。
    /// 实测这一族当前的定义数在几十个量级，整族被删掉时"未调用 0 处"仍然成立，所以单独钉一条。
    /// </summary>
    [Fact]
    public void LayoutMethodNameTable_StillMatchesWhatIsOnDisk()
    {
        var repoRoot = RepoRoot();
        var text = string.Concat(EnumerateCSharpFiles(repoRoot, ComponentDirectories).Select(ReadText));

        var unusedNames = LayoutMethodNames.Where(name => !text.Contains($"{name}(", StringComparison.Ordinal)).ToList();
        Assert.True(
            unusedNames.Count == 0,
            $"这些方法名在组件目录里已经不存在了，名字表该一起收（不然这条判据就只覆盖剩下的那几个）：" +
            string.Join(", ", unusedNames.Order(StringComparer.Ordinal)));
    }

    private static HashSet<string> CountExternalInvocations(string repoRoot)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in EnumerateCSharpFiles(repoRoot, ScanDirectories))
        {
            var text = ReadText(path);
            foreach (var name in LayoutMethodNames)
            {
                if (Regex.IsMatch(text, @"\." + Regex.Escape(name) + @"\s*\("))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    private static IEnumerable<string> EnumerateCSharpFiles(string root, IEnumerable<string> topDirs)
    {
        foreach (var directory in topDirs)
        {
            var full = Path.Combine(root, directory);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    private static string ReadText(string path) => File.ReadAllText(path);

    private static IEnumerable<(string Line, int Number)> ReadLines(string path)
    {
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            yield return (line, ++number);
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
