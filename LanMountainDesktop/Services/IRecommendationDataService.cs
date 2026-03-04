using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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

public sealed record RecommendationBackendOptions
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:5057";

    public string DailyArtworkPath { get; init; } = "/api/recommendation/daily-artwork";

    public string DailyPoetryPath { get; init; } = "/api/recommendation/daily-poetry";

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

public sealed class RecommendationBackendService : IRecommendationInfoService, IDisposable
{
    private sealed record DailyArtworkCacheEntry(DailyArtworkSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record DailyPoetryCacheEntry(DailyPoetrySnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record ArtworkCandidate(
        string Title,
        string? Artist,
        string? Year,
        string? ArtworkUrl,
        string? ImageId);

    private readonly RecommendationBackendOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly object _cacheGate = new();
    private DailyArtworkCacheEntry? _dailyArtworkCache;
    private DailyPoetryCacheEntry? _dailyPoetryCache;

    public RecommendationBackendService(
        RecommendationBackendOptions? options = null,
        HttpClient? httpClient = null)
    {
        _options = options ?? new RecommendationBackendOptions();
        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = _options.RequestTimeout
            };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public void ClearCache()
    {
        lock (_cacheGate)
        {
            _dailyArtworkCache = null;
            _dailyPoetryCache = null;
        }
    }

    public async Task<RecommendationQueryResult<DailyPoetrySnapshot>> GetDailyPoetryAsync(
        DailyPoetryQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new DailyPoetryQuery();
        if (!normalizedQuery.ForceRefresh && TryGetDailyPoetryFromCache(out var cached))
        {
            return RecommendationQueryResult<DailyPoetrySnapshot>.Ok(cached);
        }

        var uri = BuildDailyPoetryUri(normalizedQuery.Locale, normalizedQuery.ForceRefresh);
        string responseText;

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return await TryDirectPoetryFallbackAsync(
                    normalizedQuery,
                    "http_error",
                    $"HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}",
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await TryDirectPoetryFallbackAsync(
                normalizedQuery,
                "network_error",
                ex.Message,
                cancellationToken);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            var success = ReadBool(root, "success");
            if (!success.GetValueOrDefault())
            {
                return await TryDirectPoetryFallbackAsync(
                    normalizedQuery,
                    ReadString(root, "errorCode") ?? "upstream_error",
                    ReadString(root, "errorMessage") ?? "Recommendation backend returned an unsuccessful response.",
                    cancellationToken);
            }

            if (!root.TryGetProperty("data", out var dataNode) || dataNode.ValueKind != JsonValueKind.Object)
            {
                return await TryDirectPoetryFallbackAsync(
                    normalizedQuery,
                    "parse_error",
                    "Daily poetry payload is missing.",
                    cancellationToken);
            }

            var content = ReadString(dataNode, "content");
            if (string.IsNullOrWhiteSpace(content))
            {
                return await TryDirectPoetryFallbackAsync(
                    normalizedQuery,
                    "parse_error",
                    "Poetry content is missing.",
                    cancellationToken);
            }

            var snapshot = new DailyPoetrySnapshot(
                Provider: ReadString(dataNode, "provider") ?? "RecommendationBackend",
                Content: content.Trim(),
                Origin: ReadString(dataNode, "origin"),
                Author: ReadString(dataNode, "author"),
                Category: ReadString(dataNode, "category"),
                FetchedAt: ParseDateTimeOffset(ReadString(dataNode, "fetchedAt")) ?? DateTimeOffset.UtcNow);

            SetDailyPoetryCache(snapshot);
            return RecommendationQueryResult<DailyPoetrySnapshot>.Ok(snapshot);
        }
        catch (Exception ex)
        {
            return await TryDirectPoetryFallbackAsync(
                normalizedQuery,
                "parse_error",
                ex.Message,
                cancellationToken);
        }
    }

    public async Task<RecommendationQueryResult<DailyArtworkSnapshot>> GetDailyArtworkAsync(
        DailyArtworkQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new DailyArtworkQuery();
        if (!normalizedQuery.ForceRefresh && TryGetDailyArtworkFromCache(out var cached))
        {
            return RecommendationQueryResult<DailyArtworkSnapshot>.Ok(cached);
        }

        var uri = BuildDailyArtworkUri(normalizedQuery.Locale, normalizedQuery.ForceRefresh);
        string responseText;

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return await TryDirectFallbackAsync(
                    normalizedQuery,
                    "http_error",
                    $"HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}",
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await TryDirectFallbackAsync(
                normalizedQuery,
                "network_error",
                ex.Message,
                cancellationToken);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            var success = ReadBool(root, "success");
            if (!success.GetValueOrDefault())
            {
                return await TryDirectFallbackAsync(
                    normalizedQuery,
                    ReadString(root, "errorCode") ?? "upstream_error",
                    ReadString(root, "errorMessage") ?? "Recommendation backend returned an unsuccessful response.",
                    cancellationToken);
            }

            if (!root.TryGetProperty("data", out var dataNode) || dataNode.ValueKind != JsonValueKind.Object)
            {
                return await TryDirectFallbackAsync(
                    normalizedQuery,
                    "parse_error",
                    "Daily artwork payload is missing.",
                    cancellationToken);
            }

            var title = ReadString(dataNode, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                return await TryDirectFallbackAsync(
                    normalizedQuery,
                    "parse_error",
                    "Artwork title is missing.",
                    cancellationToken);
            }

            var snapshot = new DailyArtworkSnapshot(
                Provider: ReadString(dataNode, "provider") ?? "RecommendationBackend",
                Title: title.Trim(),
                Artist: ReadString(dataNode, "artist"),
                Year: ReadString(dataNode, "year"),
                Museum: ReadString(dataNode, "museum"),
                ArtworkUrl: ReadString(dataNode, "artworkUrl"),
                ImageUrl: ReadString(dataNode, "imageUrl"),
                FetchedAt: ParseDateTimeOffset(ReadString(dataNode, "fetchedAt")) ?? DateTimeOffset.UtcNow);

            SetDailyArtworkCache(snapshot);
            return RecommendationQueryResult<DailyArtworkSnapshot>.Ok(snapshot);
        }
        catch (Exception ex)
        {
            return await TryDirectFallbackAsync(
                normalizedQuery,
                "parse_error",
                ex.Message,
                cancellationToken);
        }
    }

    private async Task<RecommendationQueryResult<DailyArtworkSnapshot>> TryDirectFallbackAsync(
        DailyArtworkQuery query,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var fallback = await GetDailyArtworkDirectAsync(query, cancellationToken);
        if (fallback.Success && fallback.Data is not null)
        {
            SetDailyArtworkCache(fallback.Data);
            return fallback;
        }

        var fallbackMessage = string.IsNullOrWhiteSpace(fallback.ErrorMessage)
            ? "Direct upstream fallback failed."
            : fallback.ErrorMessage;
        return RecommendationQueryResult<DailyArtworkSnapshot>.Fail(
            errorCode,
            $"{errorMessage}; fallback: {fallbackMessage}");
    }

    private async Task<RecommendationQueryResult<DailyArtworkSnapshot>> GetDailyArtworkDirectAsync(
        DailyArtworkQuery query,
        CancellationToken cancellationToken)
    {
        var candidateCount = Math.Clamp(_options.DefaultArtworkCandidateCount, 10, 100);
        var localDate = GetChinaLocalDate();
        var page = Math.Clamp((localDate.DayOfYear % 100) + 1, 1, 100);
        var requestUrl = string.Format(
            CultureInfo.InvariantCulture,
            _options.ArtInstituteArtworkApiTemplate,
            page,
            candidateCount);

        string responseText;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return RecommendationQueryResult<DailyArtworkSnapshot>.Fail(
                    "upstream_http_error",
                    $"HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("upstream_network_error", ex.Message);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
            {
                return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("upstream_parse_error", "Artwork list is missing.");
            }

            var candidates = new List<ArtworkCandidate>();
            foreach (var item in dataArray.EnumerateArray())
            {
                var title = ReadString(item, "title");
                var imageId = ReadString(item, "image_id");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(imageId))
                {
                    continue;
                }

                var artist = ReadString(item, "artist_title");
                if (string.IsNullOrWhiteSpace(artist))
                {
                    artist = ReadFirstNonEmptyLine(ReadString(item, "artist_display"));
                }

                candidates.Add(new ArtworkCandidate(
                    title.Trim(),
                    artist,
                    ReadString(item, "date_display"),
                    ReadString(item, "api_link"),
                    imageId.Trim()));
            }

            if (candidates.Count == 0)
            {
                return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("upstream_empty_result", "No artwork candidates were returned.");
            }

            var indexSeed = localDate.Year * 1000 + localDate.DayOfYear;
            var selected = candidates[Math.Abs(indexSeed) % candidates.Count];
            var snapshot = new DailyArtworkSnapshot(
                Provider: "ArtInstituteOfChicago",
                Title: selected.Title,
                Artist: selected.Artist,
                Year: selected.Year,
                Museum: "The Art Institute of Chicago",
                ArtworkUrl: selected.ArtworkUrl,
                ImageUrl: BuildArtworkImageUrl(selected.ImageId),
                FetchedAt: DateTimeOffset.UtcNow);

            return RecommendationQueryResult<DailyArtworkSnapshot>.Ok(snapshot);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("upstream_parse_error", ex.Message);
        }
    }

    private async Task<RecommendationQueryResult<DailyPoetrySnapshot>> TryDirectPoetryFallbackAsync(
        DailyPoetryQuery query,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var fallback = await GetDailyPoetryDirectAsync(query, cancellationToken);
        if (fallback.Success && fallback.Data is not null)
        {
            SetDailyPoetryCache(fallback.Data);
            return fallback;
        }

        var fallbackMessage = string.IsNullOrWhiteSpace(fallback.ErrorMessage)
            ? "Direct upstream fallback failed."
            : fallback.ErrorMessage;
        return RecommendationQueryResult<DailyPoetrySnapshot>.Fail(
            errorCode,
            $"{errorMessage}; fallback: {fallbackMessage}");
    }

    private async Task<RecommendationQueryResult<DailyPoetrySnapshot>> GetDailyPoetryDirectAsync(
        DailyPoetryQuery query,
        CancellationToken cancellationToken)
    {
        _ = query;

        string responseText;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.JinriShiciPoetryUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return RecommendationQueryResult<DailyPoetrySnapshot>.Fail(
                    "upstream_http_error",
                    $"HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyPoetrySnapshot>.Fail("upstream_network_error", ex.Message);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var content = ReadString(root, "content");
            if (string.IsNullOrWhiteSpace(content))
            {
                return RecommendationQueryResult<DailyPoetrySnapshot>.Fail(
                    "upstream_parse_error",
                    "Poetry content is empty.");
            }

            var snapshot = new DailyPoetrySnapshot(
                Provider: "JinriShici",
                Content: content.Trim(),
                Origin: ReadString(root, "origin"),
                Author: ReadString(root, "author"),
                Category: ReadString(root, "category"),
                FetchedAt: DateTimeOffset.UtcNow);

            return RecommendationQueryResult<DailyPoetrySnapshot>.Ok(snapshot);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyPoetrySnapshot>.Fail("upstream_parse_error", ex.Message);
        }
    }

    private Uri BuildDailyArtworkUri(string? locale, bool forceRefresh)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var path = _options.DailyArtworkPath.StartsWith("/", StringComparison.Ordinal)
            ? _options.DailyArtworkPath
            : $"/{_options.DailyArtworkPath}";
        var localePart = string.IsNullOrWhiteSpace(locale)
            ? string.Empty
            : $"locale={Uri.EscapeDataString(locale.Trim())}&";
        var forcePart = forceRefresh ? "true" : "false";
        return new Uri($"{baseUrl}{path}?{localePart}forceRefresh={forcePart}", UriKind.Absolute);
    }

    private Uri BuildDailyPoetryUri(string? locale, bool forceRefresh)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var path = _options.DailyPoetryPath.StartsWith("/", StringComparison.Ordinal)
            ? _options.DailyPoetryPath
            : $"/{_options.DailyPoetryPath}";
        var localePart = string.IsNullOrWhiteSpace(locale)
            ? string.Empty
            : $"locale={Uri.EscapeDataString(locale.Trim())}&";
        var forcePart = forceRefresh ? "true" : "false";
        return new Uri($"{baseUrl}{path}?{localePart}forceRefresh={forcePart}", UriKind.Absolute);
    }

    private bool TryGetDailyArtworkFromCache(out DailyArtworkSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            if (_dailyArtworkCache is not null && _dailyArtworkCache.ExpireAt > DateTimeOffset.UtcNow)
            {
                snapshot = _dailyArtworkCache.Snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    private void SetDailyArtworkCache(DailyArtworkSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            _dailyArtworkCache = new DailyArtworkCacheEntry(
                snapshot,
                DateTimeOffset.UtcNow.Add(_options.CacheDuration));
        }
    }

    private bool TryGetDailyPoetryFromCache(out DailyPoetrySnapshot snapshot)
    {
        lock (_cacheGate)
        {
            if (_dailyPoetryCache is not null && _dailyPoetryCache.ExpireAt > DateTimeOffset.UtcNow)
            {
                snapshot = _dailyPoetryCache.Snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    private void SetDailyPoetryCache(DailyPoetrySnapshot snapshot)
    {
        lock (_cacheGate)
        {
            _dailyPoetryCache = new DailyPoetryCacheEntry(
                snapshot,
                DateTimeOffset.UtcNow.Add(_options.CacheDuration));
        }
    }

    private static string? ReadString(JsonElement node, params string[] path)
    {
        var target = TryGetNode(node, path);
        if (!target.HasValue)
        {
            return null;
        }

        return target.Value.ValueKind switch
        {
            JsonValueKind.String => target.Value.GetString(),
            JsonValueKind.Number => target.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement node, params string[] path)
    {
        var target = TryGetNode(node, path);
        if (!target.HasValue)
        {
            return null;
        }

        return target.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(target.Value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static JsonElement? TryGetNode(JsonElement node, params string[] path)
    {
        var current = node;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private string? BuildArtworkImageUrl(string? imageId)
    {
        if (string.IsNullOrWhiteSpace(imageId))
        {
            return null;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            _options.ArtInstituteImageUrlTemplate,
            imageId.Trim());
    }

    private static string? ReadFirstNonEmptyLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
    }

    private static DateOnly GetChinaLocalDate()
    {
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        return DateOnly.FromDateTime(now.Date);
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength
            ? text
            : $"{text[..maxLength]}...";
    }
}
