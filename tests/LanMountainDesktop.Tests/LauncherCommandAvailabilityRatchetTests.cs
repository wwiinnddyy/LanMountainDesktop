using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 启动器自造 <c>RelayCommand</c> 的"可用态会不会重算"守卫。
///
/// 为什么要有：这个 <c>RelayCommand</c> 不是 CommunityToolkit 的那个，它多一个
/// <c>RaiseCanExecuteChanged()</c> 方法——而 <c>ICommand</c> 里没有这个约定，
/// Avalonia 不会替你调：写了带条件的 <c>CanExecute</c> 却没人 raise，按钮就会停在第一次算出来的状态。
/// 2026-09-23 实测：启动器 7 处 <c>new RelayCommand(…)</c> **一个都没传 canExecute**，
/// 所以今天没有症状（零调用的 <c>RaiseCanExecuteChanged</c> 因此留在零使用名单里，理由就是这个）。
/// 这条守卫钉的是"以后有人加了带条件的命令，就必须同时接上重算"——
/// 那才是真会咬人的形态，而且不报错。
/// </summary>
public sealed class LauncherCommandAvailabilityRatchetTests
{
    private static readonly Regex ExplicitConstruction = new(
        @"\bnew\s+RelayCommand(?:<[^>]*>)?\s*\(", RegexOptions.Compiled);

    // 目标类型写法 `= new(() => { }, () => true)`：Mutation 试出来第一版判据漏了它。
    // 只看 `new(` 会把上百处无关构造算进来，所以要求同一行还留着 RelayCommand 这个词
    // （属性声明 `public RelayCommand Foo { get; } = new(…)` 就是这个形状）。
    private static readonly Regex TargetTypedConstruction = new(@"\bnew\s*\(", RegexOptions.Compiled);

    private static readonly Regex RelayCommandMention = new(@"\bRelayCommand\b", RegexOptions.Compiled);

    [Fact]
    public void EveryPredicateBearingLauncherCommand_HasAWayToRecomputeAvailability()
    {
        var repoRoot = RepoRoot();
        var launcherDir = Path.Combine(repoRoot, "desktop", "LanMountainDesktop.Launcher");
        var sources = Directory.EnumerateFiles(launcherDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .ToDictionary(path => path, path => File.ReadAllText(path));

        var withPredicate = new List<string>();
        var explicitSites = 0;
        var sites = 0;
        foreach (var (path, text) in sources)
        {
            foreach (var site in ConstructionMatches(text))
            {
                sites++;
                if (site.Explicit)
                {
                    explicitSites++;
                }

                if (CountTopLevelArguments(text, site.Index + site.Length - 1) >= 2)
                {
                    withPredicate.Add($"{Path.GetFileName(path)}:{LineNumberAt(text, site.Index)}");
                }
            }
        }

        var raises = sources
            .Where(entry => !entry.Key.EndsWith("RelayCommand.cs", StringComparison.OrdinalIgnoreCase))
            .Sum(entry => Regex.Matches(entry.Value, @"\bRaiseCanExecuteChanged\s*\(").Count);

        Assert.True(
            explicitSites >= 7,
            $"启动器里只找到 {explicitSites} 处显式 new RelayCommand（2026-09-23 实测 7 处）：" +
            "构造写法变了，这条判据的计数已经不可信，先核对再放行");

        // 今天的前提：没有带条件的命令 → 不需要 raise。
        // 一旦有人传了 canExecute，就必须有地方在条件变化时调用 RaiseCanExecuteChanged。
        Assert.True(
            withPredicate.Count == 0 || raises > 0,
            $"这些启动器命令写了 canExecute 条件（共 {withPredicate.Count} 处，扫到 {sites} 处构造），" +
            "却没有任何地方在条件变化时 RaiseCanExecuteChanged()：" +
            "按钮会停在首次算出的可用态（ICommand 没这个约定，Avalonia 不会替你调）。带条件的命令：" +
            string.Join(", ", withPredicate));
    }

    private static IEnumerable<(int Index, int Length, bool Explicit)> ConstructionMatches(string text)
    {
        foreach (Match match in ExplicitConstruction.Matches(text))
        {
            yield return (match.Index, match.Length, true);
        }

        foreach (Match match in TargetTypedConstruction.Matches(text))
        {
            var lineStart = text.LastIndexOf('\n', match.Index) + 1;
            var lineEnd = text.IndexOf('\n', match.Index);
            var line = text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)];
            if (RelayCommandMention.IsMatch(line))
            {
                yield return (match.Index, match.Length, false);
            }
        }
    }

    /// <summary>从开括号之后开始数顶层实参：括号、方括号、花括号内部（含多行 lambda 体）都不算分隔。</summary>
    private static int CountTopLevelArguments(string text, int openParenIndex)
    {
        var depth = 0;
        var commas = 0;
        for (var index = openParenIndex; index < text.Length; index++)
        {
            var character = text[index];
            switch (character)
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    if (depth == 0)
                    {
                        return commas + 1;
                    }

                    break;
                case ',' when depth == 1:
                    commas++;
                    break;
            }
        }

        return commas + 1;
    }

    private static int LineNumberAt(string text, int index)
    {
        var line = 1;
        for (var position = 0; position < index; position++)
        {
            if (text[position] == '\n')
            {
                line++;
            }
        }

        return line;
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
