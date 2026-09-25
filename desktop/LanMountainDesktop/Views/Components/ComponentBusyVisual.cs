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
/// 用户点了没反应，界面又没有任何"正在取数"的迹象（<c>Cnr</c> 与每日一词 1x1 今天就是这个形状）。
/// 但"忙的时候该不该淡、淡到哪一档"是视觉决定，不是机械收口能定的，所以这里只把<b>写法</b>收成一处：
/// <c>dimmedOpacity</c> 由调用方给，淡出判据默认就等于禁用判据（那才是同一个状态），
/// 只有确实要让两个判据分开的组件才显式传第二个位——显式传的那两家，分叉就写在调用点上了，
/// 而不是散在七份各写两行的实现里没人对账。
/// </summary>
internal static class ComponentBusyVisual
{
    /// <summary>淡出与禁用同判据（多数组件本该是这个形状）。</summary>
    public static void Apply(Button button, bool enabled, double dimmedOpacity) =>
        Apply(button, enabled, !enabled, dimmedOpacity);

    /// <summary>
    /// 禁用一个位、淡出另一个位——<paramref name="dimmed"/> 与 <paramref name="enabled"/> 故意不同时才用这一条，
    /// 并且要在调用点说清为什么（否则就该用上面那条同判据的重载）。
    /// </summary>
    public static void Apply(Button button, bool enabled, bool dimmed, double dimmedOpacity)
    {
        button.IsEnabled = enabled;
        Fade(button, dimmed, dimmedOpacity);
    }

    /// <summary>只淡出不禁用（字形、图标这类不可交互的控件用这一条）。</summary>
    public static void Fade(Visual target, bool dimmed, double dimmedOpacity) =>
        target.Opacity = dimmed ? dimmedOpacity : 1d;
}
