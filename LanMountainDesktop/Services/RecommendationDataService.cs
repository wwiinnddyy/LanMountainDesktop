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
    private const string UserAgent = "Mozilla/5.0";

    private sealed record DailyArtworkCacheEntry(DailyArtworkSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record DailyPoetryCacheEntry(DailyPoetrySnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record ArtworkCandidate(
        string Title,
        string? Artist,
        string? Year,
        string? ArtworkUrl,
        string? ImageId,
        string? ThumbnailDataUrl);

    private readonly RecommendationApiOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly AppSettingsService _appSettingsService = new();
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, DailyArtworkCacheEntry> _dailyArtworkCacheBySource =
        new(StringComparer.OrdinalIgnoreCase);
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
            _dailyArtworkCacheBySource.Clear();
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
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
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
        var mirrorSource = ResolveArtworkMirrorSource(normalizedQuery);
        if (!normalizedQuery.ForceRefresh && TryGetDailyArtworkFromCache(mirrorSource, out var cached))
        {
            return RecommendationQueryResult<DailyArtworkSnapshot>.Ok(cached);
        }

        return string.Equals(mirrorSource, DailyArtworkMirrorSources.Domestic, StringComparison.OrdinalIgnoreCase)
            ? await GetDailyArtworkFromDomesticSourceAsync(mirrorSource, cancellationToken)
            : await GetDailyArtworkFromOverseasSourceAsync(mirrorSource, cancellationToken);
    }

    private async Task<RecommendationQueryResult<DailyArtworkSnapshot>> GetDailyArtworkFromOverseasSourceAsync(
        string mirrorSource,
        CancellationToken cancellationToken)
    {
        var localDate = GetChinaLocalDate();
        try
        {
            var responseText = await FetchOverseasArtworkPayloadAsync(localDate, cancellationToken);
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
                var thumbnailDataUrl = ReadString(item, "thumbnail", "lqip");
                if (string.IsNullOrWhiteSpace(title) ||
                    (string.IsNullOrWhiteSpace(imageId) && string.IsNullOrWhiteSpace(thumbnailDataUrl)))
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
                    string.IsNullOrWhiteSpace(imageId) ? null : imageId.Trim(),
                    string.IsNullOrWhiteSpace(thumbnailDataUrl) ? null : thumbnailDataUrl.Trim()));
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
                ThumbnailDataUrl: selected.ThumbnailDataUrl,
                FetchedAt: DateTimeOffset.UtcNow);

            SetDailyArtworkCache(mirrorSource, snapshot);
            return RecommendationQueryResult<DailyArtworkSnapshot>.Ok(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("upstream_network_error", ex.Message);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("upstream_parse_error", ex.Message);
        }
    }

    private async Task<RecommendationQueryResult<DailyArtworkSnapshot>> GetDailyArtworkFromDomesticSourceAsync(
        string mirrorSource,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.DomesticArtworkApiUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return RecommendationQueryResult<DailyArtworkSnapshot>.Fail(
                    "upstream_http_error",
                    $"HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}");
            }

            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (!root.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
            {
                return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("upstream_parse_error", "Daily image list is missing.");
            }

            var candidates = images.EnumerateArray().ToArray();
            if (candidates.Length == 0)
            {
                return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("upstream_empty_result", "No daily image candidates were returned.");
            }

            var localDate = GetChinaLocalDate();
            var indexSeed = localDate.Year * 1000 + localDate.DayOfYear;
            var selected = candidates[Math.Abs(indexSeed) % candidates.Length];

            var imageUrl = BuildDomesticImageUrl(
                ReadString(selected, "url"),
                _options.DomesticArtworkHost);
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("upstream_parse_error", "Daily image URL is missing.");
            }

            var title = ReadString(selected, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                title = ExtractDomesticTitle(ReadString(selected, "copyright"));
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Bing Daily Image";
            }

            var dateText = ParseDomesticDateText(ReadString(selected, "startdate"));
            var artworkUrl = BuildDomesticImageUrl(
                ReadString(selected, "copyrightlink"),
                _options.DomesticArtworkHost);
            if (string.IsNullOrWhiteSpace(artworkUrl) ||
                artworkUrl.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                artworkUrl = null;
            }

            var snapshot = new DailyArtworkSnapshot(
                Provider: "BingCN",
                Title: title.Trim(),
                Artist: "Bing China",
                Year: dateText,
                Museum: "Bing China",
                ArtworkUrl: artworkUrl,
                ImageUrl: imageUrl,
                ThumbnailDataUrl: null,
                FetchedAt: DateTimeOffset.UtcNow);

            SetDailyArtworkCache(mirrorSource, snapshot);
            return RecommendationQueryResult<DailyArtworkSnapshot>.Ok(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("upstream_network_error", ex.Message);
        }
    }

    private bool TryGetDailyArtworkFromCache(string mirrorSource, out DailyArtworkSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            if (_dailyArtworkCacheBySource.TryGetValue(mirrorSource, out var cacheEntry) &&
                cacheEntry.ExpireAt > DateTimeOffset.UtcNow)
            {
                snapshot = cacheEntry.Snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    private void SetDailyArtworkCache(string mirrorSource, DailyArtworkSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            _dailyArtworkCacheBySource[mirrorSource] = new DailyArtworkCacheEntry(
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

    private string ResolveArtworkMirrorSource(DailyArtworkQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.MirrorSource))
        {
            return DailyArtworkMirrorSources.Normalize(query.MirrorSource);
        }

        try
        {
            var snapshot = _appSettingsService.Load();
            return DailyArtworkMirrorSources.Normalize(snapshot.DailyArtworkMirrorSource);
        }
        catch
        {
            return DailyArtworkMirrorSources.Overseas;
        }
    }

    private async Task<string> FetchOverseasArtworkPayloadAsync(DateOnly localDate, CancellationToken cancellationToken)
    {
        var candidateCount = Math.Clamp(_options.DefaultArtworkCandidateCount, 10, 100);
        var page = Math.Clamp((localDate.DayOfYear % 100) + 1, 1, 100);
        var requestUrl = string.Format(
            CultureInfo.InvariantCulture,
            _options.ArtInstituteArtworkApiTemplate,
            page,
            candidateCount);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}");
        }

        return responseText;
    }

    private static string? BuildDomesticImageUrl(string? rawValue, string fallbackHost)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var candidate = rawValue.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        if (!Uri.TryCreate(fallbackHost, UriKind.Absolute, out var hostUri))
        {
            return null;
        }

        var normalizedPath = candidate.StartsWith("/", StringComparison.Ordinal) ? candidate : $"/{candidate}";
        return new Uri(hostUri, normalizedPath).ToString();
    }

    private static string ExtractDomesticTitle(string? copyrightText)
    {
        if (string.IsNullOrWhiteSpace(copyrightText))
        {
            return string.Empty;
        }

        var compact = copyrightText.Trim();
        var bracketIndex = compact.IndexOf('(');
        if (bracketIndex <= 0)
        {
            return compact;
        }

        return compact[..bracketIndex].Trim();
    }

    private static string? ParseDomesticDateText(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate) || rawDate.Length < 8)
        {
            return null;
        }

        if (DateTime.TryParseExact(
                rawDate[..8],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return null;
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
