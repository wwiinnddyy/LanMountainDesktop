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

public sealed class RecommendationDataService : IRecommendationInfoService, IDisposable
{
    private sealed record DailyArtworkCacheEntry(DailyArtworkSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record DailyPoetryCacheEntry(DailyPoetrySnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record ArtworkCandidate(
        string Title,
        string? Artist,
        string? Year,
        string? ArtworkUrl,
        string? ImageId);

    private readonly RecommendationApiOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly object _cacheGate = new();
    private DailyArtworkCacheEntry? _dailyArtworkCache;
    private DailyPoetryCacheEntry? _dailyPoetryCache;

    public RecommendationDataService(
        RecommendationApiOptions? options = null,
        HttpClient? httpClient = null)
    {
        _options = options ?? new RecommendationApiOptions();
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

            SetDailyPoetryCache(snapshot);
            return RecommendationQueryResult<DailyPoetrySnapshot>.Ok(snapshot);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyPoetrySnapshot>.Fail("upstream_parse_error", ex.Message);
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

            SetDailyArtworkCache(snapshot);
            return RecommendationQueryResult<DailyArtworkSnapshot>.Ok(snapshot);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("upstream_parse_error", ex.Message);
        }
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
