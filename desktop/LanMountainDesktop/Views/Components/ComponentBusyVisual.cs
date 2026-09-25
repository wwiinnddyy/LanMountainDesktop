using Avalonia;
using Avalonia.Controls;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// "取数进行中 / 面板还没上台面"这件事怎么画到刷新控件上——写法的唯一一处。
///
/// 2026-09-25 收口前七个组件各写一份（<c>Baidu</c> 与 <c>Ifeng</c> 那份逐字相同，是重复普查里的一族；
/// 其余五份各漂开一点，普查看不见）：都是"设 <c>IsEnabled</c>，再按某个判据设 <c>Opacity</c>"这两句，
/// 但**每家的判据与淡出值都不一样**：
/// <list type="table">
/// <item><description>淡出值实测 <c>0.56 / 0.58 / 0.60 / 0.65 / 0.85</c> 五档。</description></item>
/// <item><description>判据实测三种：<c>Baidu</c>/<c>Ifeng</c> 用"没附着或在忙"同一个位管两件事；
/// <c>Cnr</c>/<c>DailyWord</c> 用"在忙"决定禁用、却用"没附着"决定淡出；
/// <c>Stcn24</c>/<c>DailyWord2x2</c> 只看"在忙"；<c>DailyPoetry</c> 禁用看自己的 <c>_isRefreshing</c>、淡出看附着。</description></item>
/// </list>
/// 第二种组合是会看见的：<b>按钮在忙的时候被禁用了，却还画成完全不淡</b>——
/// 用户点了没反应，界面又没有任何"正在取数"的迹象（<c>Cnr</c>、每日一词 1x1、每日诗词三家就是这个形状）。
///
/// 2026-09-26 定的两件事（这条轴上此前"等拍板"，现在由本轮选边）：
/// ① <b>忙的时候必须淡</b>——"禁用却不淡"是缺陷不是口径，所以"淡出判据默认＝禁用判据"，
/// 三家的分叉重载一并删掉：没有逃生口可走，就不会再长出五份各写两行的抄本。
/// ② <b>淡出只留一档</b>，值就是这个 <see cref="DimmedOpacity"/>。此前的 0.56 / 0.58 / 0.60 / 0.65 / 0.85
/// 里，0.85 基本看不出在忙（三家"点不动"的成因之一就是它），其余四档彼此差不到 0.1；
/// 取 0.60（落在密集区中间，改动最小）。要换档只改这一个常量，测试会要求人对着数字改。
/// </summary>
internal static class ComponentBusyVisual
{
    /// <summary>忙态淡到哪一档——唯一一处。</summary>
    public const double DimmedOpacity = 0.60;

    /// <summary>禁用与淡出同一个位：忙（或没上台面）就既点不动、也看得出来点不动。</summary>
    public static void Apply(Button button, bool enabled)
    {
        button.IsEnabled = enabled;
        Fade(button, !enabled);
    }

    /// <summary>只淡出不禁用（字形、图标这类不可交互的控件用这一条）。</summary>
    public static void Fade(Visual target, bool dimmed) =>
        target.Opacity = dimmed ? DimmedOpacity : 1d;

    /// <summary>
    /// "按钮 + 它旁边的图标"一起画忙态。每日一词 1x1 与 2x2 两家在淡出值统一之后剩下的两行
    /// 变得逐字相同（重复普查因此多出一族），所以这件事本身也是家的——两家各自只留一行转手。
    /// </summary>
    public static void ApplyToFeed(Button button, Visual icon, bool busy)
    {
        Apply(button, !busy);
        Fade(icon, busy);
    }
}
