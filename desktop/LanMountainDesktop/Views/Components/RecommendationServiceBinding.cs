using System;
using System.Threading.Tasks;

using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件被下推"推荐信息服务"时的两步只认这一处：先把服务换到自己字段上（给 <c>null</c> 就用自己的默认实例），
/// 然后**只在已经挂载到桌面的情况下**立刻刷一次。
///
/// 此前 10 个组件各写一遍同一条不变量（只有"刷新用的是哪个方法"不同），逐字面量到 3 族。
/// 顺序与那个挂载条件都是判据，各自错了都不报错：
/// ① 先刷后换 —— 刷出来的还是旧服务的数据，要等下一次时区/语言变更才自愈，症状是"换了源但卡片还是旧的";
/// ② 少了挂载判定 —— 组件还没挂上桌面就刷，刷新里要碰的控件与订阅还没准备好；
/// ③ 少了这次刷新 —— 换服务后卡片停在旧数据上，看上去像"服务没生效"。
///
/// 字段用 <c>ref</c> 传：刷新必须看到换好之后的值，这一点与 <see cref="TimeZoneServiceBinding"/> 同源。
/// 刷新仍是"发出去不管"（<c>_ = refresh()</c>）——与抄本逐字一致：同步抛出的异常照旧往上走，
/// 后台失败照旧由各自组件自己处理。**默认服务仍归各组件自己 new**（这里是收写法，不是收实例：
/// 十个 <c>RecommendationDataService</c> 实例会不会各自留一份缓存是另一件事，挂在 #G1-BC 等判断）。
/// </summary>
internal static class RecommendationServiceBinding
{
    public static void Attach(
        ref IRecommendationInfoService field,
        IRecommendationInfoService? next,
        IRecommendationInfoService fallback,
        Func<bool> isAttached,
        Func<Task> refresh)
    {
        field = next ?? fallback;
        if (!isAttached())
        {
            return;
        }

        _ = refresh();
    }
}
