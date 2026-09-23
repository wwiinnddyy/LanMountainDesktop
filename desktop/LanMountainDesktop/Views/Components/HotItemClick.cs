using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using LanMountainDesktop.Helpers;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// "点一条热搜条目就打开它的外链"这件事只认这一家。收口前两个热搜组件（百度、B站）各抄一份
/// 逐字相同的六行——症状不在报错上：少了范围那一格就是点到最后一条越界抛 <c>ArgumentOutOfRangeException</c>
/// （崩在点击事件里），少了左键那一格就是中键/右键点一下也弹浏览器，两种都只在用户手滑时才看得见。
///
/// 判据与取数分开：<see cref="TryGetIndex"/> 是纯判据（左键、sender 是挂点击的那个 <c>Border</c>、
/// <c>Tag</c> 是能解析且落在 <c>[0, itemCount)</c> 的下标），可注入可测；
/// <see cref="Open"/> 只是把它与真事件、真列表接起来，"打开哪条"由组件自己的列表决定。
/// <c>e.Handled</c> 只在真的打开过之后置位——原样保留抄本的语义：判据没过时不吞事件，
/// 让外层还能继续收这个点击（比如整卡片的点击行为）。
/// </summary>
internal static class HotItemClick
{
    public static bool TryGetIndex(object? sender, bool leftButtonPressed, int itemCount, out int index)
    {
        index = -1;
        if (!leftButtonPressed ||
            sender is not Border host ||
            host.Tag is null ||
            !int.TryParse(host.Tag.ToString(), out var parsed) ||
            parsed < 0 ||
            parsed >= itemCount)
        {
            return false;
        }

        index = parsed;
        return true;
    }

    public static void Open<TItem>(
        object? sender,
        PointerPressedEventArgs e,
        Visual relativeTo,
        IReadOnlyList<TItem> items,
        Func<TItem, string?> urlOf)
    {
        if (!TryGetIndex(sender, e.GetCurrentPoint(relativeTo).Properties.IsLeftButtonPressed, items.Count, out var index))
        {
            return;
        }

        ExternalLinkLauncher.TryOpen(urlOf(items[index]));
        e.Handled = true;
    }
}
