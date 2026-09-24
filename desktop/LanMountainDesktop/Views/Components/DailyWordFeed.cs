using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// "向推荐服务要当天的每日一词"只认这一处。
///
/// 1x1 与 2x2 两块面板取的是<b>同一个东西</b>：同一个接口、同一个 <see cref="DailyWordQuery"/> 形状、
/// 同一句"没成功或没数据都算这次没取到"。落地（往各自的控件上画）留给各面板自己做。
/// 2026-09-24 收 <c>RefreshWordAsync</c> 那 45 行逐字相同的抄本时量到：光把协议收走，剩下的取数与
/// "起一趟之前先画忙态"这两步仍是两块面板各写一遍——所以这两步也进家，否则只是把复制换了个地方。
/// </summary>
internal static class DailyWordFeed
{
    /// <summary>取当天的每日一词；这次没取到（接口报失败或返回空）时给 <c>null</c>，由调用方画失败态。</summary>
    internal static async Task<DailyWordSnapshot?> RequestAsync(
        IRecommendationInfoService service,
        string languageCode,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var result = await service.GetDailyWordAsync(
            new DailyWordQuery(Locale: languageCode, ForceRefresh: forceRefresh),
            cancellationToken);

        return Unwrap(result);
    }

    /// <summary>
    /// "什么算这次没取到"这条判据单独一处：接口报失败算没取到，<b>报成功但载荷是空</b>也算没取到。
    /// 后半句是抄本里原本就有的（<c>!result.Success || result.Data is null</c>），
    /// 只看 <c>Success</c> 的写法会在上游返回"成功但没词"时画出一块空白面板而不报失败。
    /// </summary>
    internal static DailyWordSnapshot? Unwrap(RecommendationQueryResult<DailyWordSnapshot> result) =>
        result.Success ? result.Data : null;

    /// <summary>
    /// 起一趟之前要做的那两下：先把按钮画成"正在刷新"，再按当前语言重读一次要用的界面文案。
    /// 顺序是判据——先读语言后画按钮的话，按钮上那句"刷新中"会停在旧语言（不报错，只是下一次才对上）。
    /// </summary>
    internal static void BeginRefresh(Action updateButtonState, Action updateLanguageCode)
    {
        updateButtonState();
        updateLanguageCode();
    }
}
