using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 学习面板"重画"这件事的口径钉（#G1-CD 收口之后）。
///
/// 2026-09-26 之前这里有三种架构并存：五个面板逐字相同地"重排 + 按当下底色重算"、
/// NoiseCurve 做同样两件事只是换拼写、SessionHistory 走快照那条路，环境面板另走 DynamicResource。
/// 现在八个里七个的 resize 走 <c>StudyComponentLifecycle.RefreshOnResize</c>，
/// "向渲染门喂一张当下快照"收成家的一处（实测此前散在 12 处 / 7 个文件）。
/// 留下来的唯一例外是环境面板，理由写在它自己的 OnSizeChanged 与家的注释里；这两条判据就是把这个例外管住。
/// </summary>
public sealed class StudyVisualRecomputeTests
{
    private static readonly string[] ExemptFromHomeResize = ["StudyEnvironmentWidget.axaml.cs"];

    // 2026-09-26 实测：八个学习面板都有 OnSizeChanged。
    private const int ResizeHandlerFloor = 8;

    private static readonly Regex HandlerStart = new(@"^\s{4}private void OnSizeChanged\(", RegexOptions.Compiled);
    private static readonly Regex MemberStart = new(
        @"^\s{4}(?:private|internal|public|protected|static|async|override|~)", RegexOptions.Compiled);
    // 家里那份用的是参数名（renderGate / studyAnalyticsService），组件里用的是 _ 前缀字段，两种写法都要认。
    private static readonly Regex QueueExpression = new(
        @"\w*[rR]enderGate\.Queue\(\w*[sS]tudyAnalyticsService\.GetSnapshot\(\)\)", RegexOptions.Compiled);

    [Fact]
    public void ResizingHandlers_AllGoThroughTheHome_ExceptTheDocumentedOne()
    {
        var offenders = new List<string>();
        var handlers = 0;

        foreach (var (file, lines) in StudyWidgetSources())
        {
            var name = Path.GetFileName(file);
            for (var index = 0; index < lines.Count; index++)
            {
                if (!HandlerStart.IsMatch(lines[index]))
                {
                    continue;
                }

                handlers++;
                var body = ReadBody(lines, index);
                var usesHome = body.Contains("StudyComponentLifecycle.RefreshOnResize", StringComparison.Ordinal);
                var exempted = ExemptFromHomeResize.Contains(name, StringComparer.OrdinalIgnoreCase);

                if (usesHome && exempted)
                {
                    offenders.Add($"{name}:{index + 1} 记为例外却又走了家——例外名单该缩掉了");
                }
                else if (!usesHome && !exempted)
                {
                    offenders.Add(
                        $"{name}:{index + 1} 自己内联了『重排 + 按当下底色重算』这两步，" +
                        "请改走 StudyComponentLifecycle.RefreshOnResize（环境面板那种确实不碰配色的，进例外名单并写理由）");
                }
            }
        }

        Assert.True(
            handlers >= ResizeHandlerFloor,
            $"只扫到 {handlers} 个 OnSizeChanged 处理器（下限 {ResizeHandlerFloor}）——" +
            "学习面板被改名或这条扫描的锚点变了，判据就只看着少数文件");
        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} 处resize 架构偏离：{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void SnapshotIsQueuedForRepaint_InExactlyOnePlace()
    {
        var insideHome = 0;
        var strays = new List<string>();

        foreach (var (file, lines) in AllStudySources())
        {
            for (var index = 0; index < lines.Count; index++)
            {
                var hits = QueueExpression.Matches(lines[index]).Count;
                if (hits == 0)
                {
                    continue;
                }

                if (Path.GetFileName(file).Equals("StudyComponentLifecycle.cs", StringComparison.OrdinalIgnoreCase))
                {
                    insideHome += hits;
                }
                else
                {
                    strays.Add($"{Path.GetFileName(file)}:{index + 1}");
                }
            }
        }

        Assert.True(
            insideHome == 1,
            $"家里喂快照应为 1 处，实测 {insideHome} 处——RequestRepaint 被复制或改名，这条判据就看不见全貌");
        Assert.True(
            strays.Count == 0,
            $"{strays.Count} 处绕开家自己喂快照（此前散在 12 处）：{string.Join(", ", strays)}");
    }

    private static IEnumerable<(string File, List<string> Lines)> StudyWidgetSources() =>
        StudyFiles("Study*Widget.axaml.cs");

    private static IEnumerable<(string File, List<string> Lines)> AllStudySources() => StudyFiles("Study*.cs");

    private static IEnumerable<(string File, List<string> Lines)> StudyFiles(string pattern)
    {
        var dir = Path.Combine(RepoRoot(), "desktop", "LanMountainDesktop", "Views", "Components");
        foreach (var file in Directory.EnumerateFiles(dir, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            yield return (file, File.ReadAllLines(file).ToList());
        }
    }

    private static string ReadBody(List<string> lines, int startIndex)
    {
        var body = new List<string>();
        for (var index = startIndex + 1; index < lines.Count; index++)
        {
            if (MemberStart.IsMatch(lines[index]))
            {
                break;
            }

            body.Add(lines[index]);
        }

        return string.Join("\n", body);
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
