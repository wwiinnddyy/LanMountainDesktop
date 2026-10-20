using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件树上的两种上下文推送：桌面页可见性与时区服务解绑。
/// 主窗口的多页桌面和单窗口桌面板都按同一棵树走，避免两边各写一遍遍历。
/// </summary>
internal static class DesktopComponentContext
{
    public static void ApplyPageContext(Control root, bool isOnActivePage, bool isEditMode)
    {
        if (root is IDesktopPageVisibilityAwareComponentWidget awareRoot)
        {
            awareRoot.SetDesktopPageContext(isOnActivePage, isEditMode);
        }

        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is IDesktopPageVisibilityAwareComponentWidget awareChild)
            {
                awareChild.SetDesktopPageContext(isOnActivePage, isEditMode);
            }
        }
    }

    public static void ClearTimeZoneServiceBindings(IEnumerable<Control> roots)
    {
        foreach (var root in roots)
        {
            ClearTimeZoneServiceBindings(root);
        }
    }

    public static void ClearTimeZoneServiceBindings(Control root)
    {
        if (root is ITimeZoneAwareComponentWidget timeZoneAwareRoot)
        {
            timeZoneAwareRoot.ClearTimeZoneService();
        }

        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is ITimeZoneAwareComponentWidget timeZoneAwareChild)
            {
                timeZoneAwareChild.ClearTimeZoneService();
            }
        }
    }
}
