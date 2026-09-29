using System;

using Avalonia.Controls;

using LanMountainDesktop.Services;
using LanMountainDesktop.Views.ComponentEditors;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件编辑器「配色档」下拉 ↔ 磁盘值那对换算的行为钉。
///
/// 收口前三个编辑器各写一份，读侧两份用 <c>IsNullOrEmpty</c>、一份用 <c>IsNullOrWhiteSpace</c>——
/// 同一个存档值在两个面板里能显示成两种档。家取的是**较宽的那一份**，所以"只含空白"这条必须钉住：
/// 把它改回 <c>IsNullOrEmpty</c> 会让三家里另外两家换口径。
/// 空白值今天没有任何写点会产出（下拉只有 <c>follow_system</c>／<c>native</c> 两个 Tag，兜底也落常量），
/// 所以这条格钉的是"家选哪一份口径"，不是"界面上今天会出什么事"。
///
/// 有一条没在这里钉：<c>ShouldUseMonetColor</c>（组件真正渲染时读这个值的地方）对"认不出的值"
/// 走的是第三种规则——退回看全局色彩档。也就是说一个存了 <c>banana</c> 的组件，面板显示「组件自定义」，
/// 渲染却可能按全局走 Monet。这条编辑器与实算的分歧登记在 #G1-CP 等拍板，下面那格只钉**现状**。
/// </summary>
public sealed class ComponentColorSchemeSelectionTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("\t", true)]
    [InlineData("follow_system", true)]
    [InlineData("FOLLOW_System", true)]
    [InlineData("native", false)]
    [InlineData("banana", false)]
    public void IsFollowSystem_ClassifiesTheStoredValue(string? stored, bool expected)
        => Assert.Equal(expected, ComponentColorSchemeSelection.IsFollowSystem(stored));

    [Fact]
    public void Resolve_KeepsTheSelectedTag()
    {
        var native = new ComboBoxItem { Tag = ThemeAppearanceValues.ColorSchemeNative };

        Assert.Equal(
            ThemeAppearanceValues.ColorSchemeNative,
            ComponentColorSchemeSelection.Resolve(native));
    }

    /// <summary>
    /// 认不出选项时落「跟随系统」，而不是落 null、也不抛。
    /// 三种形态都钉：没选（null）、选项不是 <see cref="ComboBoxItem"/>（直接放个字符串、
    /// 或放一个同样带 Tag 的其它控件）、选项没带 Tag——它们都得回到同一个值，
    /// 否则下次读盘时又变成第二种口径。带 Tag 的其它控件那一格钉的是"只认 ComboBoxItem"：
    /// 把判据放宽到 <c>ContentControl</c> 之类，前两种形态照样绿，只有这一格会红。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("native")]
    public void Resolve_FallsBackToFollowSystemForAnythingItDoesNotRecognize(object? selected)
        => Assert.Equal(
            ThemeAppearanceValues.ColorSchemeFollowSystem,
            ComponentColorSchemeSelection.Resolve(selected));

    [Fact]
    public void Resolve_IgnoresATagThatIsNotOnAComboBoxItem()
    {
        var taggedButNotAChoice = new Label { Tag = ThemeAppearanceValues.ColorSchemeNative };

        Assert.Equal(
            ThemeAppearanceValues.ColorSchemeFollowSystem,
            ComponentColorSchemeSelection.Resolve(taggedButNotAChoice));
    }

    [Fact]
    public void Resolve_TagWithoutAString_FallsBackToFollowSystem()
    {
        var tagged = new ComboBoxItem { Tag = 7 };

        Assert.Equal(
            ThemeAppearanceValues.ColorSchemeFollowSystem,
            ComponentColorSchemeSelection.Resolve(tagged));
    }

    /// <summary>
    /// 两个方向不许互相打脸：从某个档写出盘、再读回来，必须还认得是同一个档。
    /// 漂开的形状是"写侧多认一档、读侧没跟上"，症状是存完重开面板显示成另一档。
    /// </summary>
    [Theory]
    [InlineData(ThemeAppearanceValues.ColorSchemeFollowSystem, true)]
    [InlineData(ThemeAppearanceValues.ColorSchemeNative, false)]
    public void TheTwoDirections_AgreeOnEverySelectableTag(string tag, bool expectedFollowSystem)
    {
        var item = new ComboBoxItem { Tag = tag };

        Assert.Equal(tag, ComponentColorSchemeSelection.Resolve(item));
        Assert.Equal(expectedFollowSystem, ComponentColorSchemeSelection.IsFollowSystem(tag));
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
