using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.RecommendationBackend.Models;

namespace LanMountainDesktop.RecommendationBackend.Services;

public sealed record DailyQuoteQuery(
    string? Locale = null,
    bool ForceRefresh = false);

public sealed record DailyPoetryQuery(
    string? Locale = null,
    bool ForceRefresh = false);

public sealed record DailyMovieQuery(
    string? Locale = null,
    int CandidateCount = 20,
    bool ForceRefresh = false);

public sealed record DailyArtworkQuery(
    string? Locale = null,
    int CandidateCount = 50,
    bool ForceRefresh = false);

public sealed record HotSearchQuery(
    string Provider = "Baidu",
    int Limit = 10,
    bool ForceRefresh = false);

public sealed record RecommendationFeedQuery(
    string? Locale = null,
    int HotSearchLimit = 10,
    bool ForceRefresh = false);

public sealed record RecommendationQueryResult<T>(
    bool Success,
    T? Data,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static RecommendationQueryResult<T> Ok(T data)
    {
        return new RecommendationQueryResult<T>(true, data);
    }

    public static RecommendationQueryResult<T> Fail(string errorCode, string errorMessage)
    {
        return new RecommendationQueryResult<T>(false, default, errorCode, errorMessage);
    }
}

public interface IRecommendationInfoService
{
    Task<RecommendationQueryResult<DailyQuoteSnapshot>> GetDailyQuoteAsync(
        DailyQuoteQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<DailyPoetrySnapshot>> GetDailyPoetryAsync(
        DailyPoetryQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<DailyMovieRecommendation>> GetDailyMovieAsync(
        DailyMovieQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<DailyArtworkSnapshot>> GetDailyArtworkAsync(
        DailyArtworkQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<IReadOnlyList<HotSearchEntry>>> GetHotSearchAsync(
        HotSearchQuery query,
        CancellationToken cancellationToken = default);
}

public interface IRecommendationDataService : IRecommendationInfoService
{
    Task<RecommendationQueryResult<RecommendationFeedSnapshot>> GetFeedAsync(
        RecommendationFeedQuery query,
        CancellationToken cancellationToken = default);

    void ClearCache();
}
