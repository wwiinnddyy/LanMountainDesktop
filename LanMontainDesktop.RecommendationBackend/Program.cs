using System;
using LanMontainDesktop.RecommendationBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IRecommendationDataService>(serviceProvider =>
{
    var options = builder.Configuration.GetSection("Recommendation").Get<RecommendationApiOptions>();
    return new RecommendationDataService(options);
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    service = "LanMontainDesktop.RecommendationBackend",
    status = "ok",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet(
    "/api/recommendation/daily-quote",
    async (IRecommendationDataService service, string? locale, bool forceRefresh = false, CancellationToken cancellationToken = default) =>
    {
        var result = await service.GetDailyQuoteAsync(new DailyQuoteQuery(locale, forceRefresh), cancellationToken);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });

app.MapGet(
    "/api/recommendation/daily-poetry",
    async (IRecommendationDataService service, string? locale, bool forceRefresh = false, CancellationToken cancellationToken = default) =>
    {
        var result = await service.GetDailyPoetryAsync(new DailyPoetryQuery(locale, forceRefresh), cancellationToken);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });

app.MapGet(
    "/api/recommendation/daily-movie",
    async (IRecommendationDataService service, string? locale, int candidateCount = 20, bool forceRefresh = false, CancellationToken cancellationToken = default) =>
    {
        var result = await service.GetDailyMovieAsync(
            new DailyMovieQuery(locale, candidateCount <= 0 ? 20 : candidateCount, forceRefresh),
            cancellationToken);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });

app.MapGet(
    "/api/recommendation/daily-artwork",
    async (IRecommendationDataService service, string? locale, int candidateCount = 50, bool forceRefresh = false, CancellationToken cancellationToken = default) =>
    {
        var result = await service.GetDailyArtworkAsync(
            new DailyArtworkQuery(locale, candidateCount <= 0 ? 50 : candidateCount, forceRefresh),
            cancellationToken);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });

app.MapGet(
    "/api/recommendation/hot-search",
    async (IRecommendationDataService service, string? provider, int limit = 10, bool forceRefresh = false, CancellationToken cancellationToken = default) =>
    {
        var result = await service.GetHotSearchAsync(
            new HotSearchQuery(provider ?? "Baidu", limit <= 0 ? 10 : limit, forceRefresh),
            cancellationToken);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });

app.MapGet(
    "/api/recommendation/feed",
    async (IRecommendationDataService service, string? locale, int hotSearchLimit = 10, bool forceRefresh = false, CancellationToken cancellationToken = default) =>
    {
        var result = await service.GetFeedAsync(
            new RecommendationFeedQuery(locale, hotSearchLimit <= 0 ? 10 : hotSearchLimit, forceRefresh),
            cancellationToken);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    });

app.MapPost(
    "/api/recommendation/cache/clear",
    (IRecommendationDataService service) =>
    {
        service.ClearCache();
        return Results.Ok(new
        {
            success = true,
            message = "Recommendation cache cleared.",
            timestamp = DateTimeOffset.UtcNow
        });
    });

app.MapGet("/", () => Results.Redirect("/health"));

app.Run();
