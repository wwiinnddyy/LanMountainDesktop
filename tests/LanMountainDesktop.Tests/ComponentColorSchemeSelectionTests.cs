using System;

using Avalonia.Controls;

using LanMountainDesktop.Services;
using LanMountainDesktop.Views.ComponentEditors;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件编辑器「配色档」下拉 ↔ 磁盘值那套换算的行为钉。
///
/// 收口前三个编辑器各写一份，读侧两份用 <c>IsNullOrEmpty</c>、一份用 <c>IsNullOrWhiteSpace</c>——
/// 同一个存档值在两个面板里能显示成两种档。家取的是**较宽的那一份**，所以"只含空白"这条必须钉住：
/// 把它改回 <c>IsNullOrEmpty</c> 会让三家里另外两家换口径。
///
/// 2026-09-30 起下拉是**三档**（#G1-CP 定下来的形状）：渲染侧 <c>ShouldUseMonetColor</c> 对同一个磁盘值
/// 本来就有三种走法（<c>native</c> 永不走 Monet／<c>follow_system</c> 永远走／其余退回看全局色彩档），
/// 而下拉只有两档，于是每个没配过的组件都"面板说一档、画面是另一档"。第三档落盘的是**空串**——
/// 那正是"从没设置过"的值，所以加这一档不改任何像素，只改面板怎么说自己。
/// 画面那一半由 <c>ComponentColorSchemeHelperTests</c> 钉着（<c>null + default_neutral → false</c> 那一格），
/// 这里钉的是"面板的第三档与那个 null 是同一个磁盘值"。
/// </summary>
public sealed class ComponentColorSchemeSelectionTests
{
    /// <summary>判档与渲染侧同一条规则：<b>没设置、只含空白、认不出的串都落第三档</b>；
    /// 两个具名值大小写都不敏感。"   " 那一格钉的是家的口径——今天没有任何写点产出空白值，
    /// 把判据改回 <c>IsNullOrEmpty</c> 恰好红它。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("banana")]
    public void UnsetAndUnrecognizedLandOnTheThirdItem(string? stored)
    {
        var (defaultItem, followSystemItem, nativeItem) = NewItems();

        Assert.Same(
            defaultItem,
            ComponentColorSchemeSelection.ResolveSelection(stored, defaultItem, followSystemItem, nativeItem));
    }

    [Theory]
    [InlineData("follow_system")]
    [InlineData("FOLLOW_System")]
    public void FollowSystemValueLandsOnItsOwnItem(string stored)
    {
        var (defaultItem, followSystemItem, nativeItem) = NewItems();

        Assert.Same(
            followSystemItem,
            ComponentColorSchemeSelection.ResolveSelection(stored, defaultItem, followSystemItem, nativeItem));
    }

    [Theory]
    [InlineData("native")]
    [InlineData("Native")]
    public void NativeValueLandsOnItsOwnItem(string stored)
    {
        var (defaultItem, followSystemItem, nativeItem) = NewItems();

        Assert.Same(
            nativeItem,
            ComponentColorSchemeSelection.ResolveSelection(stored, defaultItem, followSystemItem, nativeItem));
    }

    /// <summary>
    /// 三档往返还对得上：写侧产出的值再读回来必须还是同一档。漂开的形状是"写侧多认一档、读侧没跟上"，
    /// 症状是存完重开面板显示成另一档。第三档那一格同时钉住**它落盘的是空串**——
    /// 换成任何一个别的哨兵（"default"／"global"…）就成了渲染侧三种走法之外的第四种值，
    /// 而它认不出的串一律退回全局档，等于把"未设置"重新变成一个有写法分歧的概念。
    /// </summary>
    [Fact]
    public void TheTwoDirections_AgreeOnEverySelectableTag()
    {
        var (defaultItem, followSystemItem, nativeItem) = NewItems();
        var items = new[] { defaultItem, followSystemItem, nativeItem };

        foreach (var item in items)
        {
            var written = ComponentColorSchemeSelection.Resolve(item);

            Assert.Same(
                item,
                ComponentColorSchemeSelection.ResolveSelection(written, defaultItem, followSystemItem, nativeItem));
        }

        Assert.Equal(string.Empty, ComponentColorSchemeSelection.Resolve(defaultItem));
    }

    [Fact]
    public void Resolve_KeepsTheSelectedTag()
    {
        var native = new ComboBoxItem { Tag = ThemeAppearanceValues.ColorSchemeNative };

        Assert.Equal(
            ThemeAppearanceValues.ColorSchemeNative,
            ComponentColorSchemeSelection.Resolve(native));
    }

    /// <summary>
    /// 认不出选项时落空串（＝第三档），而不是落 <c>follow_system</c>、也不是 null、也不抛。
    /// 2026-09-30 之前这里落的是「跟随系统」，那是两档时代的兜底；三档之后"认不出"与"没设置"
    /// 必须是同一个值，否则它与上面那格判档就不是同一条规则了。
    /// 三种形态都钉：没选（null）、选项不是 <see cref="ComboBoxItem"/>、选项没带字符串 Tag——
    /// 其中"带 Tag 但不是 ComboBoxItem"那一格钉的是"只认 ComboBoxItem"：
    /// 把判据放宽到 <c>ContentControl</c> 之类，前两种形态照样绿，只有这一格会红。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("native")]
    public void Resolve_FallsBackToTheUnsetValueForAnythingItDoesNotRecognize(object? selected)
        => Assert.Equal(string.Empty, ComponentColorSchemeSelection.Resolve(selected));

    [Fact]
    public void Resolve_IgnoresATagThatIsNotOnAComboBoxItem()
    {
        var taggedButNotAChoice = new Label { Tag = ThemeAppearanceValues.ColorSchemeNative };

        Assert.Equal(string.Empty, ComponentColorSchemeSelection.Resolve(taggedButNotAChoice));
    }

    [Fact]
    public void Resolve_TagThatIsNotAString_FallsBackToTheUnsetValue()
    {
        var tagged = new ComboBoxItem { Tag = 7 };

        Assert.Equal(string.Empty, ComponentColorSchemeSelection.Resolve(tagged));
    }

    /// <summary>
    /// 这一族不许再在编辑器里各写一遍：除家以外，<c>Views/ComponentEditors</c> 里不许再出现那两个常量名。
    /// 判据先在真树上绿过（现在三家都走家），再种一个违规验它会红。
    /// </summary>
    [Fact]
    public void ColorSchemeMapping_LivesOnlyInTheHome()
    {
        var editorDirectory = Path.Combine(RepoRoot(), "desktop", "LanMountainDesktop", "Views", "ComponentEditors");
        Assert.True(Directory.Exists(editorDirectory), $"找不到编辑器目录 {editorDirectory}：判据看不见东西时的绿不算证据");

        var files = Directory.GetFiles(editorDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFileName(file), "ComponentColorSchemeSelection.cs", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            files.Count >= 15,
            $"这一跑只扫到 {files.Count} 个编辑器代码文件（2026-09-30 实测 16 个，不含家自己）" +
            "——目录改名或筛选写坏会让这条判据安静地失业");

        var offenders = files
            .Where(file => File.ReadLines(file).Any(line => line.Contains(
                "ThemeAppearanceValues.ColorScheme", StringComparison.Ordinal)))
            .Select(file => Path.GetFileName(file))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "又把配色档的字符串映射写回编辑器里了：" + string.Join(", ", offenders.Order(StringComparer.Ordinal)) +
            "。三份抄本里两份用 IsNullOrEmpty、一份用 IsNullOrWhiteSpace，就是这么攒出来的——改判据请连家一起改");
    }

    /// <summary>
    /// 第三档必须在**三家都存在**，而且 <c>Tag</c> 必须就是空串：这一族的"面板说的是哪一档"最终落在
    /// 三个 .axaml 里的那一行上，家改了、标记没改，症状就是某个编辑器的下拉仍然只有两项。
    /// 判据按文件点名（少一家就红并报出文件名），并钉 <c>Tag=""</c> 的写法——
    /// 把它换成 <c>Tag="default"</c> 会让第三档变成渲染侧认不出的第四种值。
    /// </summary>
    [Theory]
    [InlineData("ClassScheduleComponentEditor")]
    [InlineData("RemovableStorageComponentEditor")]
    [InlineData("StudyEnvironmentComponentEditor")]
    public void EveryEditor_OffersTheThirdItemWithTheUnsetTag(string editor)
    {
        var path = Path.Combine(
            RepoRoot(), "desktop", "LanMountainDesktop", "Views", "ComponentEditors", $"{editor}.axaml");
        Assert.True(File.Exists(path), $"{editor}.axaml 不在了：这条判据看不见东西时的绿不算证据");

        var markup = File.ReadAllText(path);
        var itemStart = markup.IndexOf("x:Name=\"DefaultColorSchemeItem\"", StringComparison.Ordinal);
        Assert.True(itemStart >= 0, $"{editor} 的下拉里没有第三档（默认／按全局配色档）那一项");

        var itemEnd = markup.IndexOf("/>", itemStart, StringComparison.Ordinal);
        Assert.True(itemEnd > itemStart, $"{editor} 的第三档那一项没写完");
        Assert.Contains(
            "Tag=\"\"",
            markup.AsSpan(itemStart, itemEnd - itemStart).ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>三档各一个项，Tag 与三家 XAML 里的写法逐字相同（第三档是空串）。
    /// 夹具在测试方法里现造：AvaloniaObject 带线程亲和，跨用例共享静态实例会把线程检查引进来。</summary>
    private static (ComboBoxItem Default, ComboBoxItem FollowSystem, ComboBoxItem Native) NewItems() =>
    (
        new ComboBoxItem { Tag = string.Empty },
        new ComboBoxItem { Tag = ThemeAppearanceValues.ColorSchemeFollowSystem },
        new ComboBoxItem { Tag = ThemeAppearanceValues.ColorSchemeNative }
    );

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
