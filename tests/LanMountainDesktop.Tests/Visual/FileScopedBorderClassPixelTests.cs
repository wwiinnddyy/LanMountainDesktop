using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.Views;
using LanMountainDesktop.Views.Components;
using LanMountainDesktop.Views.SettingsPages;
using Xunit;

namespace LanMountainDesktop.Tests.Visual;

/// <summary>
/// 另一半像素普查：<b>文件作用域</b>（窗口 / 控件自己的 <c>&lt;*.Styles&gt;</c>）里给 Border 类
/// 设了 <c>Background</c> 的那些样式——<see cref="BorderStyleClassPixelTests"/> 只扫 <c>App.axaml</c>
/// 引入的字典，这 9 个类在它视野外（2026-09-22 数出来的：App 级 5 个、文件级 9 个）。
///
/// 判据是"真实例 + 解析出的画刷必须可见"：把控件/窗口按生产方式构造出来、贴上主题资源、走完一次布局，
/// 然后要求带这个类的那个元素解析出的 <c>Background</c> 非空、非 Transparent、不透明度 &gt; 0。
/// <b>不</b>比对两帧像素：真实例里的几何取决于数据（进度条宽度、列表有没有项），
/// 拿"贴类与不贴类像素不同"判会造出偶发红灯——那种比对留给 <see cref="BorderStyleClassPixelTests"/>
/// 那种人造语境（一个孤零零的 Border，几何由测试定死）。
///
/// 为什么值得写：2026-09-21 组件库预览卡的 <c>AdaptiveCardBackgroundBrush</c> 没人注册，
/// 兜底成 <c>Brushes.Transparent</c>，不报错不抛异常，那块 UI 只是静默失色。
/// "有人要某个 Adaptive 键、没人注册"由 <c>CapabilityEntryPointTests</c> 的键覆盖探针管；
/// 这条管的是"贴了这个类的元素，最后到底有没有拿到能看见的底"。
/// </summary>
public sealed class FileScopedBorderClassPixelTests
{
    private static readonly ThemeColorContext JudgeContext = new(
        Color.FromRgb(0x40, 0x80, 0xC0),
        IsLightBackground: true,
        IsLightNavBackground: true,
        IsNightMode: false);

    /// <summary>已经能按真实例判的类 → 怎么把那个真实例造出来。</summary>
    private static readonly Dictionary<string, Func<object>> Covered = new(StringComparer.Ordinal)
    {
        ["music-progress-track"] = () => new MusicControlWidget(),
        ["music-progress-fill"] = () => new MusicControlWidget(),
        ["notification-card"] = () => new NotificationWindow(),
        ["about-hero-card"] = () => new AboutSettingsPage(),
    };

    /// <summary>还没覆盖的类，每条写清挡在哪；理由对应的类一旦从磁盘消失就红。</summary>
    private static readonly Dictionary<string, string> NotCoveredYet = new(StringComparer.Ordinal)
    {
        ["component-editor-card"] = "画刷由 ComponentEditorWindow.SetBrushResource 按调色板运行期写进编辑器窗口；"
            + "想不起真实例就绕不过去——实测把它那份 Styles/ComponentEditorThemeResources.axaml 单独 StyleInclude 进裸窗口，"
            + "解析就抛 KeyNotFoundException: Static resource 'MaterialRadioButton' not found（字典里的样式依赖 Material 主题），"
            + "所以这条只能在带 Material 主题的真实例上判",
        ["component-editor-hero-card"] = "同 component-editor-card（同一份字典、同一批画刷）",
        ["component-editor-segmented-host"] = "同 component-editor-card（同一份字典、同一批画刷）",
        ["taskbar-profile-popup-panel"] = "样式在 Views/MainWindow.axaml 的 <Window.Styles>，画刷由 "
            + "MainWindow.ApplyTaskbarProfilePopupTheme 运行期写入；造真实例等于把整个主窗口的服务图起起来",
        ["taskbar-profile-popup-avatar"] = "同 taskbar-profile-popup-panel",
    };

    public static TheoryData<string> CoveredClasses()
    {
        var data = new TheoryData<string>();
        foreach (var name in Covered.Keys.Order(StringComparer.Ordinal))
        {
            data.Add(name);
        }

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(CoveredClasses))]
    public void FileScopedClassResolvesAVisibleBackground(string styleClass)
    {
        var root = Covered[styleClass]();
        var window = root as Window ?? new Window { Content = (Control)root };

        // 生产里这些画刷由主题服务写到应用与窗口两级资源表上。这两行是照生产补一遍窗口级，
        // 但别把它当成判据成立的原因：2026-09-22 变异实测——把这两行整个注释掉，4 行照样全绿，
        // 因为底座（VisualTestApp）已按 #44 把 Adaptive* 注册在应用级资源表上，DynamicResource 一路找得到。
        // 所以本判据真正依赖的是"应用级注册 + 文件作用域样式落得上"，摘掉任一环才会红。
        ThemeColorSystemService.ApplyThemeResources(window.Resources, JudgeContext);
        GlassEffectService.ApplyGlassResources(window.Resources, JudgeContext);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var target = window.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains(styleClass));

        Assert.NotNull(target);
        var background = target!.Background
            ?? throw new Xunit.Sdk.XunitException($".{styleClass} 解析出的 Background 为空：样式没落上还是 DynamicResource 键没注册？");
        if (background is ISolidColorBrush solid)
        {
            Assert.True(
                solid.Color != Colors.Transparent && solid.Opacity > 0,
                $".{styleClass} 的背景是 {solid.Color}（不透明度 {solid.Opacity}）——那块 UI 静默失色");
        }

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// 覆盖面账本。锚点是"磁盘上有这条 <c>Border.类</c> 样式且它设了 Background"，
    /// 所以改名、删样式、或者悄悄从名单里掉出去都会让账对不上——判据静默失效比红灯贵。
    /// </summary>
    [Fact]
    public void CoverageLedger_MatchesTheStylesOnDisk()
    {
        var onDisk = ScanFileScopedClasses();
        var tracked = Covered.Keys.Concat(NotCoveredYet.Keys).ToHashSet(StringComparer.Ordinal);

        var untracked = onDisk.Where(name => !tracked.Contains(name)).Order(StringComparer.Ordinal).ToArray();
        var staleCovered = Covered.Keys.Where(name => !onDisk.Contains(name)).Order(StringComparer.Ordinal).ToArray();
        var staleUncovered = NotCoveredYet.Keys
            .Where(name => !onDisk.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            untracked.Length == 0 && staleCovered.Length == 0 && staleUncovered.Length == 0,
            $"文件作用域 Border 类的账本对不上（磁盘 {onDisk.Count} 个：已判 {Covered.Count}、登记待判 {NotCoveredYet.Count}）。"
            + Environment.NewLine + $"没登记的新类：{string.Join(", ", untracked)}"
            + Environment.NewLine + $"已判名单里磁盘上没有的：{string.Join(", ", staleCovered)}"
            + Environment.NewLine + $"理由条目已失效（类不在了）：{string.Join(", ", staleUncovered)}");
    }

    private static HashSet<string> ScanFileScopedClasses()
    {
        var hostDirectory = Path.Combine(RepoRoot, "desktop", "LanMountainDesktop");
        var stylesDirectory = Path.Combine(hostDirectory, "Styles");
        var appIncluded = Regex
            .Matches(
                File.ReadAllText(Path.Combine(hostDirectory, "App.axaml")),
                @"Source=""avares://LanMountainDesktop/Styles/([\w.]+\.axaml)""")
            .Select(match => Path.GetFullPath(Path.Combine(stylesDirectory, match.Groups[1].Value)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(hostDirectory, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsBuildArtifactPath(file) || appIncluded.Contains(Path.GetFullPath(file)))
            {
                continue;
            }

            foreach (Match style in Regex.Matches(
                         File.ReadAllText(file),
                         @"<Style Selector=""Border\.([\w-]+)"">([\s\S]*?)</Style>"))
            {
                if (Regex.IsMatch(style.Groups[2].Value, @"Property=""Background"""))
                {
                    found.Add(style.Groups[1].Value);
                }
            }
        }

        return found;
    }

    private static bool IsBuildArtifactPath(string path) =>
        Regex.IsMatch(path, @"[\\/](obj|bin)[\\/]");

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? AppContext.BaseDirectory;
        }
    }
}
