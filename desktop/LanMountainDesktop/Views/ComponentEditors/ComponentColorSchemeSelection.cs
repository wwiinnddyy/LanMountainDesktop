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
/// **三档的形状是 2026-09-30 定下来的（#G1-CP）**：下拉以前只有两档，而渲染侧
/// <c>ComponentSystem/ComponentColorSchemeHelper.ShouldUseMonetColor</c> 对同一个值有三种走法——
/// <c>native</c> 永不走 Monet、<c>follow_system</c> 永远走 Monet、**其余（没设置、空白、认不出的串）
/// 退回看全局色彩档**。两档配三种走法，结果每一个刚放上桌面、没动过下拉的组件都是
/// "面板说一档、画面是另一档"。第三档 <see cref="ComponentColorSchemeChoice.Default"/> 就是那条第三种走法，
/// 它落盘的是空白（与"从没设置过"同一个值），所以加这一档**一个像素都不改**：
/// 改的只有面板怎么说自己。
/// </summary>
public static class ComponentColorSchemeSelection
{
    /// <summary>磁盘上的那个值属于三档里的哪一档。认不出的串一律算 <see cref="Choice.Default"/>——
    /// 与渲染侧同一条规则（它也是先认两个具名值、其余退回全局档）。
    /// 判档不对外开：外面只关心"选哪一项、落什么盘"，把第三种口径留在家里。</summary>
    private static Choice Classify(string? colorSchemeSource)
    {
        if (string.Equals(colorSchemeSource, ThemeAppearanceValues.ColorSchemeNative, StringComparison.OrdinalIgnoreCase))
        {
            return Choice.Native;
        }

        if (string.Equals(colorSchemeSource, ThemeAppearanceValues.ColorSchemeFollowSystem, StringComparison.OrdinalIgnoreCase))
        {
            return Choice.FollowSystem;
        }

        return Choice.Default;
    }

    /// <summary>把磁盘值翻成下拉该选中哪一项。三家各传自己那三个 <see cref="ComboBoxItem"/>，
    /// 判档只有上面那一处——症状防的是"改了一家的映射、另两家不动"。</summary>
    public static ComboBoxItem ResolveSelection(
        string? colorSchemeSource,
        ComboBoxItem defaultItem,
        ComboBoxItem followSystemItem,
        ComboBoxItem nativeItem)
        => Classify(colorSchemeSource) switch
        {
            Choice.Native => nativeItem,
            Choice.FollowSystem => followSystemItem,
            _ => defaultItem
        };

    /// <summary>把下拉的选中项换回要落盘的值。第三项的 Tag 是空串——那就是"从没设置过"，
    /// 渲染侧照旧按全局配色档走。认不出选项时也落空串（同判档那一处：认不出＝没设置），
    /// 不抛。</summary>
    public static string Resolve(object? selectedItem)
        => selectedItem is ComboBoxItem item && item.Tag is string tag
            ? tag
            : string.Empty;

    /// <summary>三档：组件默认（按全局配色档）／跟随系统／组件自定义。</summary>
    private enum Choice
    {
        Default,
        FollowSystem,
        Native
    }
}
