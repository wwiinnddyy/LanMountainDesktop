using Avalonia.Controls;
using Avalonia.Media;

namespace LanMountainDesktop.Theme;

/**
 * 读自适应主题资源只认这一处口径。此前宿主里有 5 份私有实现，各自问不同的地方：
 * MainWindow 只看自己那本字典、圆角助手只看 Application、组件们沿树往上找 ——
 * 同一个键在 A 处取得到、在 B 处取不到，而取不到的表现是"那块 UI 静默失色"，
 * 既不报错也没有编译期信号。统一成作用域最广的"沿资源作用域往上找"，
 * 取不到时用什么兜底仍由调用方决定（那些兜底色本来就是各块 UI 自己的口径）。
 */
public static class AdaptiveTokens
{
    /// <summary>沿 source 的资源作用域往上找一个键；找不到返回 false（不猜、不兜底）。
    /// source 可以是控件、Application 或某本资源字典 —— 它们在同一条资源作用域链上。</summary>
    public static bool TryGet<T>(IResourceHost source, string key, out T value)
    {
        if (source.TryFindResource(key, out var found) && found is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// 取一个主题画笔。<paramref name="opacity"/> 只作用在纯色画笔上
    /// （渐变画笔没有"整体不透明度"这一说，原样返回）。
    /// </summary>
    public static IBrush Brush(IResourceHost source, string key, IBrush fallback, double? opacity = null)
    {
        var brush = TryGet<IBrush>(source, key, out var found) ? found : fallback;
        return opacity is null || brush is not ISolidColorBrush solid
            ? brush
            : new SolidColorBrush(solid.Color, opacity.Value);
    }
}
