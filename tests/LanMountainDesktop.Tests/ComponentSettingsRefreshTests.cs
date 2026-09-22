using System.Text.RegularExpressions;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件"设置变了重读一遍"这条入口的证据：走子树的家 + 15 个组件都认这个契约 + 宿主真的会走。
///
/// 为什么单独钉一条：2026-09-22 实测 15 个组件各自写了 <c>public void RefreshFromSettings()</c>，
/// 全仓没有任何宿主调用点（只有 4 个在自己的 <c>SetComponentPlacementContext</c> 里自调一次）。
/// 宿主的设置重载走的是"就地刷新"（<c>ReloadFromPersistedSettings</c> 只做
/// <c>ApplyLocalization</c> + <c>RebuildDesktopGrid</c>，后者只算几何、不重建控件），
/// 所以切语言后已经放在桌面上的组件会一直留着旧语言的文案，直到被重新添加或重启。
/// 这类"实现了但没入口"的东西，光靠删死码的尺子会被误删，所以入口本身要钉住。
/// </summary>
public sealed class ComponentSettingsRefreshTests
{
    [AvaloniaFact]
    public void RefreshAll_ReachesTheRootAndEveryNestedWidget()
    {
        var nested = new AwarePanel();
        var root = new AwarePanel();
        root.Children.Add(new Border { Child = nested });
        root.Children.Add(new TextBlock());

        ComponentSettingsRefresh.RefreshAll(root);

        Assert.Equal(1, root.RefreshCount);
        Assert.Equal(1, nested.RefreshCount);
    }

    [AvaloniaFact]
    public void RefreshAll_SkipsControlsThatDidNotOptIn()
    {
        var host = new Panel();
        host.Children.Add(new Border { Child = new TextBlock() });

        var error = Record.Exception(() => ComponentSettingsRefresh.RefreshAll(host));

        Assert.Null(error);
    }

    [Fact]
    public void EveryWidgetWithRefreshFromSettings_DeclaresTheContract()
    {
        var repoRoot = RepoRoot();
        var declares = new Regex(@"^\s*public\s+(?:async\s+)?void\s+RefreshFromSettings\s*\(\s*\)",
            RegexOptions.Multiline | RegexOptions.Compiled);
        var offenders = new List<string>();
        var total = 0;

        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(repoRoot, "desktop", "LanMountainDesktop", "Views", "Components"),
                     "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            if (!declares.IsMatch(text))
            {
                continue;
            }

            total++;
            if (!text.Contains("ISettingsAwareComponentWidget", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(path));
            }
        }

        Assert.True(
            total >= 15,
            $"只找到 {total} 个组件实现 RefreshFromSettings()（2026-09-22 实测是 15 个）：" +
            "要么契约被整批删掉，要么扫描范围变了——先查清再放行");
        Assert.True(
            offenders.Count == 0,
            "这些组件写了 RefreshFromSettings() 却没声明 ISettingsAwareComponentWidget，" +
            "宿主的刷新走查不到它们，等于又把它变成死约定：" + string.Join(", ", offenders.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void HostSettingsReloadPath_RunsTheRefreshWalk_OnlyWhenTheLanguageChanged()
    {
        var repoRoot = RepoRoot();
        var reload = File.ReadAllText(Path.Combine(
            repoRoot, "desktop", "LanMountainDesktop", "Views", "MainWindow.SettingsHardCut.Stubs.cs"));
        var walker = File.ReadAllText(Path.Combine(
            repoRoot, "desktop", "LanMountainDesktop", "Views", "MainWindow.ComponentSystem.cs"));

        var capture = reload.IndexOf("var languageCodeBeforeReload = _languageCode;", StringComparison.Ordinal);
        var applyLocalization = reload.IndexOf("ApplyLocalization();", StringComparison.Ordinal);
        var gate = reload.IndexOf("languageCodeBeforeReload, _languageCode", StringComparison.Ordinal);
        var walk = reload.IndexOf("RefreshAttachedComponentWidgetsFromSettings();", StringComparison.Ordinal);

        Assert.True(capture >= 0, "重载路径没有先记下旧语言码：那道闸门就无从比较");
        Assert.True(
            reload.IndexOf("InitializeLocalization(snapshot.LanguageCode);", StringComparison.Ordinal) > capture,
            "旧语言码必须在 InitializeLocalization 之后才读——在那之前读，闸门永远是 false，刷新等于没接");
        Assert.True(
            gate > capture && gate < walk && walk > applyLocalization,
            "宿主要么没在 ApplyLocalization 之后刷新已附着组件，要么把刷新挂在了\"任何设置变更\"上：" +
            "实测 15 个实现里有 9 个带 forceRefresh 的第三方拉取，挂宽了＝每改一次设置打一轮网络");
        Assert.Contains("ComponentSettingsRefresh.RefreshAll(", walker, StringComparison.Ordinal);
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

    private sealed class AwarePanel : Panel, ISettingsAwareComponentWidget
    {
        public int RefreshCount { get; private set; }

        public void RefreshFromSettings() => RefreshCount++;
    }
}
