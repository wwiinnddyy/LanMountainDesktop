using System;

using Avalonia.Controls;

using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.ComponentEditors;

/// <summary>
/// 组件编辑器的「配色档」下拉与磁盘上那个字符串之间的换算，只认这一处。
///
/// 收口前三个编辑器各写一遍同一个映射（课程表、学习环境、可移动存储）：读侧把磁盘值翻成下拉选中项，
/// 写侧把选中项的 Tag 翻回磁盘值。**两份抄本已经漂开**——读侧判"没设置"时两个用
/// <see cref="string.IsNullOrEmpty(string)"/>、一个用 <see cref="string.IsNullOrWhiteSpace(string)"/>，
/// 于是一个只含空白的存档在两个编辑器里显示成两种档（症状不是崩，是同一份设置换个面板说法不同）。
///
/// 有一条**故意没在这里统一**：组件真正渲染时读这个值的不是编辑器，而是
/// `ComponentSystem/ComponentColorSchemeHelper.ShouldUseMonetColor`，它对"没设置"走的是第三种规则
/// （不认 follow_system 也不认 native 时，退回看全局色彩档）。所以"未设置的组件在面板里显示成跟随系统、
/// 实际按全局档渲染"这条分歧今天还在——统一它要先定"未设置到底算哪一档"，属产品口径，
/// 已登记为 #G1-CP，不在这一笔里代做。
/// </summary>
public static class ComponentColorSchemeSelection
{
    /// <summary>磁盘上这个值算不算「跟随系统」档。空白与没设置都算（三个编辑器里取最宽的那一份口径）。</summary>
    public static bool IsFollowSystem(string? colorSchemeSource)
        => string.IsNullOrWhiteSpace(colorSchemeSource)
           || string.Equals(
                  colorSchemeSource,
                  ThemeAppearanceValues.ColorSchemeFollowSystem,
                  StringComparison.OrdinalIgnoreCase);

    /// <summary>把下拉的选中项换回要落盘的值；认不出选项时落「跟随系统」，不抛。</summary>
    public static string Resolve(object? selectedItem)
        => selectedItem is ComboBoxItem item && item.Tag is string tag
            ? tag
            : ThemeAppearanceValues.ColorSchemeFollowSystem;
}
