using System;
using System.Threading.Tasks;

using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// <see cref="DailyWordFeed"/> 的判据。1x1 与 2x2 两块每日一词面板共用这一个取数入口，
/// 所以"什么算这次没取到"与"起一趟之前先做哪两下、按什么顺序"只能有一份说法。
/// 不钉 <c>RequestAsync</c> 那一层对服务的调用（它只是组 query 再交给 <see cref="DailyWordFeed.Unwrap"/>），
/// 因为给 <c>IRecommendationInfoService</c> 造一个假实现要填十几个成员——判据都在 <c>Unwrap</c> 上，钉那里。
/// </summary>
public sealed class DailyWordFeedTests
{
    private static readonly DailyWordSnapshot Snapshot = new(
        "provider",
        "词",
        "uk",
        "us",
        "释义",
        "例句",
        "译文",
        "https://example.test/word",
        new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Unwrap_KeepsThePayload_WhenTheQuerySucceededWithWords()
    {
        Assert.Same(Snapshot, DailyWordFeed.Unwrap(new RecommendationQueryResult<DailyWordSnapshot>(true, Snapshot)));
    }

    [Fact]
    public void Unwrap_TreatsASuccessfulButEmptyPayloadAsNothingFound()
    {
        // 只看 Success 的写法会在这里画出一块空白面板而不报失败——上游确实会给"成功但没词"。
        Assert.Null(DailyWordFeed.Unwrap(new RecommendationQueryResult<DailyWordSnapshot>(true, null)));
    }

    [Fact]
    public void Unwrap_TreatsAFailedQueryAsNothingFound_EvenWhenItCarriesData()
    {
        Assert.Null(DailyWordFeed.Unwrap(new RecommendationQueryResult<DailyWordSnapshot>(false, Snapshot, "500", "上游炸了")));
    }

    [Fact]
    public void BeginRefresh_PaintsBusyBeforeRereadingLanguage()
    {
        var order = string.Empty;

        DailyWordFeed.BeginRefresh(() => order += "button", () => order += "language");

        // 顺序反过来不报错，只会让按钮上那句"正在刷新"停在旧语言，要到下一次才对上。
        Assert.Equal("buttonlanguage", order);
    }
}
