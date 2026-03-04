using System;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed record DailyArtworkQuery(
    string? Locale = null,
    bool ForceRefresh = false);

public sealed record DailyPoetryQuery(
    string? Locale = null,
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

public sealed record RecommendationApiOptions
{
    public string JinriShiciPoetryUrl { get; init; } = "https://v1.jinrishici.com/all.json";

    public string ArtInstituteArtworkApiTemplate { get; init; } =
        "https://api.artic.edu/api/v1/artworks?page={0}&limit={1}&fields=id,title,artist_title,artist_display,date_display,image_id,api_link";

    public string ArtInstituteImageUrlTemplate { get; init; } =
        "https://www.artic.edu/iiif/2/{0}/full/843,/0/default.jpg";

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public int DefaultArtworkCandidateCount { get; init; } = 50;
}

public interface IRecommendationInfoService
{
    Task<RecommendationQueryResult<DailyArtworkSnapshot>> GetDailyArtworkAsync(
        DailyArtworkQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<DailyPoetrySnapshot>> GetDailyPoetryAsync(
        DailyPoetryQuery query,
        CancellationToken cancellationToken = default);

    void ClearCache();
}
