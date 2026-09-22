using Avalonia.Controls;
using Avalonia.VisualTree;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 把一个组件子树里所有"设置敏感"的控件拉回自己的设置。与
/// <c>ComponentPreviewRuntimeQuiescer</c> / <c>MainWindow</c> 里那几次"整棵子树推一遍"同形状：
/// 组件外面还包着 chrome（外层 Border + 内容宿主），所以根和后代都要查，不能只看直接子控件。
/// </summary>
internal static class ComponentSettingsRefresh
{
    public static void RefreshAll(Control componentRoot)
    {
        ArgumentNullException.ThrowIfNull(componentRoot);

        if (componentRoot is ISettingsAwareComponentWidget awareRoot)
        {
            awareRoot.RefreshFromSettings();
        }

        foreach (var descendant in componentRoot.GetVisualDescendants())
        {
            if (descendant is ISettingsAwareComponentWidget awareChild)
            {
                awareChild.RefreshFromSettings();
            }
        }
    }
}
