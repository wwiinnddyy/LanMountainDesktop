using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LanMontainDesktop.RecommendationBackend.Models;

namespace LanMontainDesktop.RecommendationBackend.Services;

public sealed record RecommendationApiOptions
{
    public string DailyQuoteUrl { get; init; } = "https://v1.hitokoto.cn/?encode=json&charset=utf-8";

    public string DailyPoetryUrl { get; init; } = "https://v1.jinrishici.com/all.json";

    public string DoubanHotMovieUrlTemplate { get; init; } =
        "https://movie.douban.com/j/search_subjects?type=movie&tag=%E7%83%AD%E9%97%A8&page_limit={0}&page_start=0";

    public string BaiduHotSearchUrl { get; init; } = "https://top.baidu.com/board?tab=realtime";

    public string ArtInstituteArtworkApiTemplate { get; init; } =
        "https://api.artic.edu/api/v1/artworks?page={0}&limit={1}&fields=id,title,artist_title,artist_display,date_display,image_id,api_link";

    public string ArtInstituteImageUrlTemplate { get; init; } =
        "https://www.artic.edu/iiif/2/{0}/full/843,/0/default.jpg";

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public int DefaultMovieCandidateCount { get; init; } = 20;

    public int DefaultHotSearchLimit { get; init; } = 10;

    public int DefaultArtworkCandidateCount { get; init; } = 50;
}

public sealed class RecommendationDataService : IRecommendationDataService, IDisposable
{
    private sealed record CacheEntry(object Value, DateTimeOffset ExpireAt);

    private sealed record MovieCandidate(
        string Title,
        string? Rating,
        string? Url,
        string? CoverUrl);

    private sealed record ArtworkCandidate(
        string Title,
        string? Artist,
        string? Year,
        string? ArtworkUrl,
        string? ImageId);

    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex HotSearchSplitRegex = new("<div\\s+class=\"category-wrap_[^\"]+\"[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RankRegex = new("<div\\s+class=\"index_[^\"]+\"[^>]*>\\s*(?<value>\\d+)\\s*</div>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TitleRegex = new("<div\\s+class=\"c-single-text-ellipsis\"[^>]*>\\s*(?<value>.*?)\\s*</div>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex UrlRegex = new("<a\\s+href=\"(?<value>https?://[^\"]+)\"\\s+class=\"title_[^\"]*\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HotValueRegex = new("<div\\s+class=\"hot-index_[^\"]+\"[^>]*>\\s*(?<value>[\\d,]+)\\s*</div>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SummaryRegex = new("<div\\s+class=\"hot-desc_[^\"]+\"[^>]*>\\s*(?<value>.*?)(?:<a|</div>)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private readonly RecommendationApiOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

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
            _cache.Clear();
        }
    }

    public async Task<RecommendationQueryResult<DailyQuoteSnapshot>> GetDailyQuoteAsync(
        DailyQuoteQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new DailyQuoteQuery();
        var locale = string.IsNullOrWhiteSpace(normalizedQuery.Locale) ? "zh-CN" : normalizedQuery.Locale.Trim();
        var cacheKey = $"daily_quote|{locale}";

        if (!normalizedQuery.ForceRefresh && TryGetCached(cacheKey, out DailyQuoteSnapshot cached))
        {
            return RecommendationQueryResult<DailyQuoteSnapshot>.Ok(cached);
        }

        string responseText;
        try
        {
            responseText = await FetchTextAsync(new Uri(_options.DailyQuoteUrl, UriKind.Absolute), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyQuoteSnapshot>.Fail("network_error", ex.Message);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            var content = ReadString(root, "hitokoto") ?? ReadString(root, "content");
            if (string.IsNullOrWhiteSpace(content))
            {
                return RecommendationQueryResult<DailyQuoteSnapshot>.Fail("parse_error", "Quote content is empty.");
            }

            var snapshot = new DailyQuoteSnapshot(
                Provider: "Hitokoto",
                Content: content.Trim(),
                Author: ReadString(root, "from_who") ?? ReadString(root, "creator"),
                Source: ReadString(root, "from"),
                FetchedAt: DateTimeOffset.UtcNow);

            SetCache(cacheKey, snapshot);
            return RecommendationQueryResult<DailyQuoteSnapshot>.Ok(snapshot);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyQuoteSnapshot>.Fail("parse_error", ex.Message);
        }
    }

    public async Task<RecommendationQueryResult<DailyPoetrySnapshot>> GetDailyPoetryAsync(
        DailyPoetryQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new DailyPoetryQuery();
        var locale = string.IsNullOrWhiteSpace(normalizedQuery.Locale) ? "zh-CN" : normalizedQuery.Locale.Trim();
        var cacheKey = $"daily_poetry|{locale}";

        if (!normalizedQuery.ForceRefresh && TryGetCached(cacheKey, out DailyPoetrySnapshot cached))
        {
            return RecommendationQueryResult<DailyPoetrySnapshot>.Ok(cached);
        }

        string responseText;
        try
        {
            responseText = await FetchTextAsync(new Uri(_options.DailyPoetryUrl, UriKind.Absolute), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyPoetrySnapshot>.Fail("network_error", ex.Message);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            var content = ReadString(root, "content");
            if (string.IsNullOrWhiteSpace(content))
            {
                return RecommendationQueryResult<DailyPoetrySnapshot>.Fail("parse_error", "Poetry content is empty.");
            }

            var snapshot = new DailyPoetrySnapshot(
                Provider: "JinriShici",
                Content: content.Trim(),
                Origin: ReadString(root, "origin"),
                Author: ReadString(root, "author"),
                Category: ReadString(root, "category"),
                FetchedAt: DateTimeOffset.UtcNow);

            SetCache(cacheKey, snapshot);
            return RecommendationQueryResult<DailyPoetrySnapshot>.Ok(snapshot);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyPoetrySnapshot>.Fail("parse_error", ex.Message);
        }
    }

    public async Task<RecommendationQueryResult<DailyMovieRecommendation>> GetDailyMovieAsync(
        DailyMovieQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new DailyMovieQuery();
        var candidateCount = Math.Clamp(
            normalizedQuery.CandidateCount > 0 ? normalizedQuery.CandidateCount : _options.DefaultMovieCandidateCount,
            5,
            50);
        var localDate = GetChinaLocalDate();
        var cacheKey = $"daily_movie|{localDate:yyyyMMdd}|{candidateCount}";

        if (!normalizedQuery.ForceRefresh && TryGetCached(cacheKey, out DailyMovieRecommendation cached))
        {
            return RecommendationQueryResult<DailyMovieRecommendation>.Ok(cached);
        }

        var requestUrl = string.Format(
            CultureInfo.InvariantCulture,
            _options.DoubanHotMovieUrlTemplate,
            candidateCount);

        string responseText;
        try
        {
            responseText = await FetchTextAsync(
                new Uri(requestUrl, UriKind.Absolute),
                cancellationToken,
                request =>
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                    request.Headers.TryAddWithoutValidation("Referer", "https://movie.douban.com/");
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyMovieRecommendation>.Fail("network_error", ex.Message);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (!root.TryGetProperty("subjects", out var subjects) || subjects.ValueKind != JsonValueKind.Array)
            {
                return RecommendationQueryResult<DailyMovieRecommendation>.Fail("parse_error", "Movie list is missing.");
            }

            var candidates = new List<MovieCandidate>();
            foreach (var item in subjects.EnumerateArray())
            {
                var title = ReadString(item, "title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                candidates.Add(new MovieCandidate(
                    Title: title.Trim(),
                    Rating: ReadString(item, "rate"),
                    Url: ReadString(item, "url"),
                    CoverUrl: ReadString(item, "cover")));
            }

            if (candidates.Count == 0)
            {
                return RecommendationQueryResult<DailyMovieRecommendation>.Fail("empty_result", "No movie candidates were returned.");
            }

            var indexSeed = localDate.Year * 1000 + localDate.DayOfYear;
            var selected = candidates[Math.Abs(indexSeed) % candidates.Count];

            var snapshot = new DailyMovieRecommendation(
                Provider: "Douban",
                Title: selected.Title,
                Rating: selected.Rating,
                Description: "豆瓣热门电影每日推荐",
                Url: selected.Url,
                CoverUrl: selected.CoverUrl,
                FetchedAt: DateTimeOffset.UtcNow);

            SetCache(cacheKey, snapshot);
            return RecommendationQueryResult<DailyMovieRecommendation>.Ok(snapshot);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyMovieRecommendation>.Fail("parse_error", ex.Message);
        }
    }

    public async Task<RecommendationQueryResult<DailyArtworkSnapshot>> GetDailyArtworkAsync(
        DailyArtworkQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new DailyArtworkQuery();
        var candidateCount = Math.Clamp(
            normalizedQuery.CandidateCount > 0 ? normalizedQuery.CandidateCount : _options.DefaultArtworkCandidateCount,
            10,
            100);
        var localDate = GetChinaLocalDate();
        var page = Math.Clamp((localDate.DayOfYear % 100) + 1, 1, 100);
        var cacheKey = $"daily_artwork|{localDate:yyyyMMdd}|p{page}|n{candidateCount}";

        if (!normalizedQuery.ForceRefresh && TryGetCached(cacheKey, out DailyArtworkSnapshot cached))
        {
            return RecommendationQueryResult<DailyArtworkSnapshot>.Ok(cached);
        }

        var requestUrl = string.Format(
            CultureInfo.InvariantCulture,
            _options.ArtInstituteArtworkApiTemplate,
            page,
            candidateCount);

        string responseText;
        try
        {
            responseText = await FetchTextAsync(
                new Uri(requestUrl, UriKind.Absolute),
                cancellationToken,
                request => request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("network_error", ex.Message);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
            {
                return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("parse_error", "Artwork list is missing.");
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
                    Title: title.Trim(),
                    Artist: artist,
                    Year: ReadString(item, "date_display"),
                    ArtworkUrl: ReadString(item, "api_link"),
                    ImageId: imageId.Trim()));
            }

            if (candidates.Count == 0)
            {
                return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("empty_result", "No artwork candidates were returned.");
            }

            var indexSeed = localDate.Year * 1000 + localDate.DayOfYear;
            var selected = candidates[Math.Abs(indexSeed) % candidates.Count];
            var imageUrl = BuildArtworkImageUrl(selected.ImageId);

            var snapshot = new DailyArtworkSnapshot(
                Provider: "ArtInstituteOfChicago",
                Title: selected.Title,
                Artist: selected.Artist,
                Year: selected.Year,
                Museum: "The Art Institute of Chicago",
                ArtworkUrl: selected.ArtworkUrl,
                ImageUrl: imageUrl,
                FetchedAt: DateTimeOffset.UtcNow);

            SetCache(cacheKey, snapshot);
            return RecommendationQueryResult<DailyArtworkSnapshot>.Ok(snapshot);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyArtworkSnapshot>.Fail("parse_error", ex.Message);
        }
    }

    public async Task<RecommendationQueryResult<IReadOnlyList<HotSearchEntry>>> GetHotSearchAsync(
        HotSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new HotSearchQuery();
        var provider = string.IsNullOrWhiteSpace(normalizedQuery.Provider)
            ? "Baidu"
            : normalizedQuery.Provider.Trim();
        var limit = Math.Clamp(
            normalizedQuery.Limit > 0 ? normalizedQuery.Limit : _options.DefaultHotSearchLimit,
            1,
            50);
        var cacheKey = $"hot_search|{provider}|{limit}";

        if (!normalizedQuery.ForceRefresh && TryGetCached(cacheKey, out IReadOnlyList<HotSearchEntry> cached))
        {
            return RecommendationQueryResult<IReadOnlyList<HotSearchEntry>>.Ok(cached);
        }

        if (!string.Equals(provider, "Baidu", StringComparison.OrdinalIgnoreCase))
        {
            return RecommendationQueryResult<IReadOnlyList<HotSearchEntry>>.Fail(
                "unsupported_provider",
                $"Unsupported hot search provider: {provider}");
        }

        string responseText;
        try
        {
            responseText = await FetchTextAsync(
                new Uri(_options.BaiduHotSearchUrl, UriKind.Absolute),
                cancellationToken,
                request => request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<IReadOnlyList<HotSearchEntry>>.Fail("network_error", ex.Message);
        }

        try
        {
            var entries = ParseBaiduHotSearch(responseText, limit);
            if (entries.Count == 0)
            {
                return RecommendationQueryResult<IReadOnlyList<HotSearchEntry>>.Fail("parse_error", "No hot search entries found.");
            }

            SetCache(cacheKey, entries);
            return RecommendationQueryResult<IReadOnlyList<HotSearchEntry>>.Ok(entries);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<IReadOnlyList<HotSearchEntry>>.Fail("parse_error", ex.Message);
        }
    }

    public async Task<RecommendationQueryResult<RecommendationFeedSnapshot>> GetFeedAsync(
        RecommendationFeedQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new RecommendationFeedQuery();
        var quoteTask = GetDailyQuoteAsync(
            new DailyQuoteQuery(normalizedQuery.Locale, normalizedQuery.ForceRefresh),
            cancellationToken);
        var poetryTask = GetDailyPoetryAsync(
            new DailyPoetryQuery(normalizedQuery.Locale, normalizedQuery.ForceRefresh),
            cancellationToken);
        var movieTask = GetDailyMovieAsync(
            new DailyMovieQuery(normalizedQuery.Locale, ForceRefresh: normalizedQuery.ForceRefresh),
            cancellationToken);
        var artworkTask = GetDailyArtworkAsync(
            new DailyArtworkQuery(normalizedQuery.Locale, ForceRefresh: normalizedQuery.ForceRefresh),
            cancellationToken);
        var hotTask = GetHotSearchAsync(
            new HotSearchQuery(Limit: normalizedQuery.HotSearchLimit, ForceRefresh: normalizedQuery.ForceRefresh),
            cancellationToken);

        await Task.WhenAll(quoteTask, poetryTask, movieTask, artworkTask, hotTask);

        var quote = quoteTask.Result;
        var poetry = poetryTask.Result;
        var movie = movieTask.Result;
        var artwork = artworkTask.Result;
        var hot = hotTask.Result;

        if (!quote.Success && !poetry.Success && !movie.Success && !artwork.Success && !hot.Success)
        {
            return RecommendationQueryResult<RecommendationFeedSnapshot>.Fail(
                "upstream_unavailable",
                "All upstream recommendation providers failed.");
        }

        var snapshot = new RecommendationFeedSnapshot(
            FetchedAt: DateTimeOffset.UtcNow,
            DailyQuote: quote.Success ? quote.Data : null,
            DailyPoetry: poetry.Success ? poetry.Data : null,
            DailyMovie: movie.Success ? movie.Data : null,
            DailyArtwork: artwork.Success ? artwork.Data : null,
            HotSearches: hot.Success && hot.Data is not null ? hot.Data : Array.Empty<HotSearchEntry>());

        return RecommendationQueryResult<RecommendationFeedSnapshot>.Ok(snapshot);
    }

    private async Task<string> FetchTextAsync(
        Uri requestUri,
        CancellationToken cancellationToken,
        Action<HttpRequestMessage>? configureRequest = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        configureRequest?.Invoke(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(content, 180)}");
        }

        return content;
    }

    private IReadOnlyList<HotSearchEntry> ParseBaiduHotSearch(string html, int limit)
    {
        var parts = HotSearchSplitRegex.Split(html);
        var entries = new List<HotSearchEntry>(limit);

        for (var i = 1; i < parts.Length; i++)
        {
            var chunk = parts[i];
            var title = DecodeHtml(ExtractGroupValue(TitleRegex, chunk, "value"));
            var url = DecodeHtml(ExtractGroupValue(UrlRegex, chunk, "value"));
            var hotValue = DecodeHtml(ExtractGroupValue(HotValueRegex, chunk, "value"));
            var summary = DecodeHtml(ExtractGroupValue(SummaryRegex, chunk, "value"));
            var rankText = ExtractGroupValue(RankRegex, chunk, "value");

            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (!int.TryParse(rankText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank))
            {
                rank = entries.Count + 1;
            }

            entries.Add(new HotSearchEntry(
                Provider: "Baidu",
                Rank: rank,
                Title: title,
                HotValue: string.IsNullOrWhiteSpace(hotValue) ? null : hotValue,
                Summary: string.IsNullOrWhiteSpace(summary) ? null : summary,
                Url: string.IsNullOrWhiteSpace(url) ? null : url));

            if (entries.Count >= limit)
            {
                break;
            }
        }

        var uniqueEntries = entries
            .GroupBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

        for (var i = 0; i < uniqueEntries.Count; i++)
        {
            var item = uniqueEntries[i];
            uniqueEntries[i] = item with { Rank = i + 1 };
        }

        return uniqueEntries;
    }

    private static string? ExtractGroupValue(Regex regex, string input, string groupName)
    {
        var match = regex.Match(input);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[groupName].Value;
    }

    private static string? DecodeHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(value);
        decoded = HtmlTagRegex.Replace(decoded, " ");
        return string.Join(" ", decoded.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
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

    private bool TryGetCached<T>(string cacheKey, out T value)
    {
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                if (entry.ExpireAt > DateTimeOffset.UtcNow && entry.Value is T typedValue)
                {
                    value = typedValue;
                    return true;
                }

                _cache.Remove(cacheKey);
            }
        }

        value = default!;
        return false;
    }

    private void SetCache(string cacheKey, object value)
    {
        var expireAt = DateTimeOffset.UtcNow.Add(_options.CacheDuration);
        lock (_cacheGate)
        {
            _cache[cacheKey] = new CacheEntry(value, expireAt);
        }
    }

    private static string? ReadString(JsonElement? node, params string[] path)
    {
        if (!node.HasValue)
        {
            return null;
        }

        var target = path.Length == 0 ? node : TryGetNode(node.Value, path);
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
