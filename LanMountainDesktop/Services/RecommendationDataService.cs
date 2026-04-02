using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed class RecommendationDataService : IRecommendationInfoService, IDisposable
{
    private const string UserAgent = "Mozilla/5.0";
    private static readonly Regex CnrListAnchorRegex = new(
        "<a\\s+href=\"(?<url>https?://[^\"]*?t\\d+_\\d+\\.shtml(?:\\?[^\"]*)?)\"[^>]*>(?<inner>.*?)</a>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex HtmlImageTagRegex = new(
        "<img[^>]+(?:src|data-src)=\"(?<url>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex RssAlternateLinkRegex = new(
        "<link[^>]+rel=\"alternate\"[^>]+type=\"(?:application/(?:rss\\+xml|atom\\+xml)|text/xml)\"[^>]+href=\"(?<url>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex RssDescriptionImageRegex = new(
        "<img[^>]+src=\"(?<url>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BaiduHotSearchHeatRegex = new(
        "^(?<keyword>.+?)\\s*热度[:：]\\s*(?<heat>\\d+)\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BaiduTopBoardDataRegex = new(
        "<!--\\s*s-data:(?<json>\\{.*?\\})\\s*-->",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex IfengNewsStreamRegex = new(
        "\"newsstream\"\\s*:\\s*(?<json>\\[.*?\\])\\s*,\\s*\"cooperation\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex HtmlTagRegex = new("<.*?>", RegexOptions.Compiled | RegexOptions.Singleline);

    private sealed record DailyArtworkCacheEntry(DailyArtworkSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record DailyPoetryCacheEntry(DailyPoetrySnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record DailyNewsCacheEntry(DailyNewsSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record IfengNewsCacheEntry(DailyNewsSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record BilibiliHotSearchCacheEntry(BilibiliHotSearchSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record BaiduHotSearchCacheEntry(BaiduHotSearchSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record DailyWordCacheEntry(DailyWordSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record Stcn24ForumPostsCacheEntry(Stcn24ForumPostsSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record ExchangeRateTableCacheEntry(
        string BaseCurrency,
        Dictionary<string, decimal> Rates,
        DateTimeOffset ExpireAt,
        DateTimeOffset FetchedAt);
    private sealed record ZhiJiaoHubCacheEntry(ZhiJiaoHubSnapshot Snapshot, DateTimeOffset ExpireAt);
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
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, DailyArtworkCacheEntry> _dailyArtworkCacheBySource =
        new(StringComparer.OrdinalIgnoreCase);
    private DailyPoetryCacheEntry? _dailyPoetryCache;
    private DailyNewsCacheEntry? _dailyNewsCache;
    private readonly Dictionary<string, IfengNewsCacheEntry> _ifengNewsCacheByChannel =
        new(StringComparer.OrdinalIgnoreCase);
    private BilibiliHotSearchCacheEntry? _bilibiliHotSearchCache;
    private readonly Dictionary<string, BaiduHotSearchCacheEntry> _baiduHotSearchCacheBySource =
        new(StringComparer.OrdinalIgnoreCase);
    private DailyWordCacheEntry? _dailyWordCache;
    private readonly Dictionary<string, Stcn24ForumPostsCacheEntry> _stcn24ForumPostsCacheBySource =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExchangeRateTableCacheEntry> _exchangeRateCacheByBaseCurrency =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ZhiJiaoHubCacheEntry> _zhiJiaoHubCacheBySource =
        new(StringComparer.OrdinalIgnoreCase);
    private int _dailyNewsRotationCursor;

    static RecommendationDataService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public RecommendationDataService(
        RecommendationApiOptions? options = null,
        HttpClient? httpClient = null)
    {
        _options = options ?? new RecommendationApiOptions();
        if (httpClient is null)
        {
            // 配置 HttpClientHandler 以支持所有 TLS 版本
            var handler = new HttpClientHandler
            {
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                               System.Security.Authentication.SslProtocols.Tls13,
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            _httpClient = new HttpClient(handler)
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
            _dailyNewsCache = null;
            _ifengNewsCacheByChannel.Clear();
            _bilibiliHotSearchCache = null;
            _baiduHotSearchCacheBySource.Clear();
            _dailyWordCache = null;
            _stcn24ForumPostsCacheBySource.Clear();
            _exchangeRateCacheByBaseCurrency.Clear();
            _zhiJiaoHubCacheBySource.Clear();
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

    public async Task<RecommendationQueryResult<DailyNewsSnapshot>> GetDailyNewsAsync(
        DailyNewsQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new DailyNewsQuery();
        var targetCount = normalizedQuery.ItemCount.HasValue
            ? Math.Clamp(normalizedQuery.ItemCount.Value, 1, 12)
            : Math.Clamp(_options.DefaultDailyNewsCount, 1, 12);

        if (!normalizedQuery.ForceRefresh &&
            TryGetDailyNewsFromCache(out var cached) &&
            cached.Items.Count >= targetCount)
        {
            var projectedSnapshot = cached with
            {
                Items = cached.Items.Take(targetCount).ToArray()
            };
            return RecommendationQueryResult<DailyNewsSnapshot>.Ok(projectedSnapshot);
        }

        try
        {
            var items = await FetchCnrDailyNewsItemsAsync(targetCount, cancellationToken);
            if (items.Count == 0)
            {
                return RecommendationQueryResult<DailyNewsSnapshot>.Fail(
                    "upstream_empty_result",
                    "No CNR news items were returned.");
            }

                        var snapshot = new DailyNewsSnapshot(
                Provider: "CNR",
                Source: "央广网·头条",
                Items: SelectDailyNewsItems(items, targetCount, normalizedQuery.ForceRefresh),
                FetchedAt: DateTimeOffset.UtcNow);

            SetDailyNewsCache(snapshot);
            return RecommendationQueryResult<DailyNewsSnapshot>.Ok(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return RecommendationQueryResult<DailyNewsSnapshot>.Fail("upstream_network_error", ex.Message);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyNewsSnapshot>.Fail("upstream_parse_error", ex.Message);
        }
    }

    public async Task<RecommendationQueryResult<DailyNewsSnapshot>> GetIfengNewsAsync(
        IfengNewsQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new IfengNewsQuery();
        var channelType = IfengNewsChannelTypes.Normalize(normalizedQuery.ChannelType);
        var targetCount = normalizedQuery.ItemCount.HasValue
            ? Math.Clamp(normalizedQuery.ItemCount.Value, 1, 12)
            : Math.Clamp(_options.DefaultIfengNewsCount, 1, 12);

        if (!normalizedQuery.ForceRefresh &&
            TryGetIfengNewsFromCache(channelType, out var cached) &&
            cached.Items.Count >= targetCount)
        {
            var projectedSnapshot = cached with
            {
                Items = cached.Items.Take(targetCount).ToArray()
            };
            return RecommendationQueryResult<DailyNewsSnapshot>.Ok(projectedSnapshot);
        }

        try
        {
            var snapshot = await FetchIfengNewsSnapshotAsync(targetCount, channelType, cancellationToken);
            if (snapshot.Items.Count == 0)
            {
                return RecommendationQueryResult<DailyNewsSnapshot>.Fail(
                    "upstream_empty_result",
                    "No ifeng news items were returned.");
            }

            SetIfengNewsCache(channelType, snapshot);
            return RecommendationQueryResult<DailyNewsSnapshot>.Ok(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return RecommendationQueryResult<DailyNewsSnapshot>.Fail("upstream_network_error", ex.Message);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<DailyNewsSnapshot>.Fail("upstream_parse_error", ex.Message);
        }
    }

    public async Task<RecommendationQueryResult<BilibiliHotSearchSnapshot>> GetBilibiliHotSearchAsync(
        BilibiliHotSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new BilibiliHotSearchQuery();
        var targetCount = normalizedQuery.ItemCount.HasValue
            ? Math.Clamp(normalizedQuery.ItemCount.Value, 1, 20)
            : Math.Clamp(_options.DefaultBilibiliHotSearchCount, 1, 20);

        if (!normalizedQuery.ForceRefresh &&
            TryGetBilibiliHotSearchFromCache(out var cached) &&
            cached.Items.Count >= targetCount)
        {
            var projectedSnapshot = cached with
            {
                Items = cached.Items.Take(targetCount).ToArray()
            };
            return RecommendationQueryResult<BilibiliHotSearchSnapshot>.Ok(projectedSnapshot);
        }

        try
        {
            var snapshot = await FetchBilibiliHotSearchSnapshotAsync(targetCount, cancellationToken);
            if (snapshot.Items.Count == 0)
            {
                return RecommendationQueryResult<BilibiliHotSearchSnapshot>.Fail(
                    "upstream_empty_result",
                    "No Bilibili hot search items were returned.");
            }

            SetBilibiliHotSearchCache(snapshot);
            return RecommendationQueryResult<BilibiliHotSearchSnapshot>.Ok(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return RecommendationQueryResult<BilibiliHotSearchSnapshot>.Fail("upstream_network_error", ex.Message);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<BilibiliHotSearchSnapshot>.Fail("upstream_parse_error", ex.Message);
        }
    }

    public async Task<RecommendationQueryResult<BaiduHotSearchSnapshot>> GetBaiduHotSearchAsync(
        BaiduHotSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new BaiduHotSearchQuery();
        var sourceType = BaiduHotSearchSourceTypes.Normalize(normalizedQuery.SourceType);
        var targetCount = normalizedQuery.ItemCount.HasValue
            ? Math.Clamp(normalizedQuery.ItemCount.Value, 1, 20)
            : Math.Clamp(_options.DefaultBaiduHotSearchCount, 1, 20);

        if (!normalizedQuery.ForceRefresh &&
            TryGetBaiduHotSearchFromCache(sourceType, out var cached) &&
            cached.Items.Count >= targetCount)
        {
            var projectedSnapshot = cached with
            {
                Items = cached.Items.Take(targetCount).ToArray()
            };
            return RecommendationQueryResult<BaiduHotSearchSnapshot>.Ok(projectedSnapshot);
        }

        try
        {
            var snapshot = await FetchBaiduHotSearchSnapshotAsync(targetCount, sourceType, cancellationToken);
            if (snapshot.Items.Count == 0)
            {
                return RecommendationQueryResult<BaiduHotSearchSnapshot>.Fail(
                    "upstream_empty_result",
                    "No Baidu hot search items were returned.");
            }

            SetBaiduHotSearchCache(sourceType, snapshot);
            return RecommendationQueryResult<BaiduHotSearchSnapshot>.Ok(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return RecommendationQueryResult<BaiduHotSearchSnapshot>.Fail("upstream_network_error", ex.Message);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<BaiduHotSearchSnapshot>.Fail("upstream_parse_error", ex.Message);
        }
    }

    public async Task<RecommendationQueryResult<DailyWordSnapshot>> GetDailyWordAsync(
        DailyWordQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new DailyWordQuery();
        if (!normalizedQuery.ForceRefresh && TryGetDailyWordFromCache(out var cached))
        {
            return RecommendationQueryResult<DailyWordSnapshot>.Ok(cached);
        }

        var candidates = BuildDailyWordCandidates();
        if (candidates.Count == 0)
        {
            return RecommendationQueryResult<DailyWordSnapshot>.Fail(
                "upstream_parse_error",
                "Youdao daily word candidates are empty.");
        }

        var startIndex = ResolveDailyWordStartIndex(candidates.Count, normalizedQuery.ForceRefresh);
        var attemptCount = Math.Min(candidates.Count, 24);
        Exception? lastError = null;

        for (var offset = 0; offset < attemptCount; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[(startIndex + offset) % candidates.Count];
            try
            {
                var snapshot = await TryFetchYoudaoDailyWordAsync(candidate, cancellationToken);
                if (snapshot is null)
                {
                    continue;
                }

                SetDailyWordCache(snapshot);
                return RecommendationQueryResult<DailyWordSnapshot>.Ok(snapshot);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        return RecommendationQueryResult<DailyWordSnapshot>.Fail(
            "upstream_empty_result",
            lastError?.Message ?? "No available daily word from Youdao.");
    }

    public async Task<RecommendationQueryResult<Stcn24ForumPostsSnapshot>> GetStcn24ForumPostsAsync(
        Stcn24ForumPostsQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new Stcn24ForumPostsQuery();
        var sourceType = Stcn24ForumSourceTypes.Normalize(normalizedQuery.SourceType);
        var targetCount = normalizedQuery.ItemCount.HasValue
            ? Math.Clamp(normalizedQuery.ItemCount.Value, 1, 12)
            : Math.Clamp(_options.DefaultStcn24ForumPostCount, 1, 12);

        if (!normalizedQuery.ForceRefresh &&
            TryGetStcn24ForumPostsFromCache(sourceType, out var cached) &&
            cached.Items.Count >= targetCount)
        {
            var projectedSnapshot = cached with
            {
                Items = cached.Items.Take(targetCount).ToArray()
            };
            return RecommendationQueryResult<Stcn24ForumPostsSnapshot>.Ok(projectedSnapshot);
        }

        try
        {
            var snapshot = await FetchStcn24ForumPostsSnapshotAsync(targetCount, sourceType, cancellationToken);
            if (snapshot.Items.Count == 0)
            {
                return RecommendationQueryResult<Stcn24ForumPostsSnapshot>.Fail(
                    "upstream_empty_result",
                    "No STCN forum posts were returned.");
            }

            SetStcn24ForumPostsCache(sourceType, snapshot);
            return RecommendationQueryResult<Stcn24ForumPostsSnapshot>.Ok(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return RecommendationQueryResult<Stcn24ForumPostsSnapshot>.Fail("upstream_network_error", ex.Message);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<Stcn24ForumPostsSnapshot>.Fail("upstream_parse_error", ex.Message);
        }
    }

    public async Task<RecommendationQueryResult<ExchangeRateSnapshot>> GetExchangeRateAsync(
        ExchangeRateQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new ExchangeRateQuery();
        var baseCurrency = NormalizeCurrencyCode(normalizedQuery.BaseCurrency, "USD");
        var targetCurrency = NormalizeCurrencyCode(normalizedQuery.TargetCurrency, "CNY");

        if (string.Equals(baseCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return RecommendationQueryResult<ExchangeRateSnapshot>.Ok(
                new ExchangeRateSnapshot(
                    Provider: "open.er-api.com",
                    Source: "open.er-api.com",
                    BaseCurrency: baseCurrency,
                    TargetCurrency: targetCurrency,
                    Rate: 1m,
                    FetchedAt: DateTimeOffset.UtcNow));
        }

        if (!normalizedQuery.ForceRefresh &&
            TryGetExchangeRateTableFromCache(baseCurrency, out var cached) &&
            cached.Rates.TryGetValue(targetCurrency, out var cachedRate) &&
            cachedRate > 0)
        {
            return RecommendationQueryResult<ExchangeRateSnapshot>.Ok(
                new ExchangeRateSnapshot(
                    Provider: "open.er-api.com",
                    Source: "open.er-api.com",
                    BaseCurrency: baseCurrency,
                    TargetCurrency: targetCurrency,
                    Rate: cachedRate,
                    FetchedAt: cached.FetchedAt));
        }

        try
        {
            var snapshot = await FetchExchangeRateSnapshotAsync(
                baseCurrency,
                targetCurrency,
                cancellationToken);
            return RecommendationQueryResult<ExchangeRateSnapshot>.Ok(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return RecommendationQueryResult<ExchangeRateSnapshot>.Fail("upstream_network_error", ex.Message);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<ExchangeRateSnapshot>.Fail("upstream_parse_error", ex.Message);
        }
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

    private bool TryGetDailyNewsFromCache(out DailyNewsSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            if (_dailyNewsCache is not null && _dailyNewsCache.ExpireAt > DateTimeOffset.UtcNow)
            {
                snapshot = _dailyNewsCache.Snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    private void SetDailyNewsCache(DailyNewsSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            _dailyNewsCache = new DailyNewsCacheEntry(
                snapshot,
                DateTimeOffset.UtcNow.Add(_options.CacheDuration));
        }
    }

    private bool TryGetIfengNewsFromCache(string channelType, out DailyNewsSnapshot snapshot)
    {
        var normalizedChannelType = IfengNewsChannelTypes.Normalize(channelType);
        lock (_cacheGate)
        {
            if (_ifengNewsCacheByChannel.TryGetValue(normalizedChannelType, out var cacheEntry) &&
                cacheEntry.ExpireAt > DateTimeOffset.UtcNow)
            {
                snapshot = cacheEntry.Snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    private void SetIfengNewsCache(string channelType, DailyNewsSnapshot snapshot)
    {
        var normalizedChannelType = IfengNewsChannelTypes.Normalize(channelType);
        lock (_cacheGate)
        {
            _ifengNewsCacheByChannel[normalizedChannelType] = new IfengNewsCacheEntry(
                snapshot,
                DateTimeOffset.UtcNow.Add(_options.CacheDuration));
        }
    }

    private async Task<DailyNewsSnapshot> FetchIfengNewsSnapshotAsync(
        int targetCount,
        string channelType,
        CancellationToken cancellationToken)
    {
        var safeCount = Math.Clamp(targetCount, 1, 12);
        var normalizedChannelType = IfengNewsChannelTypes.Normalize(channelType);
        var candidateLimit = Math.Max(8, safeCount * 3);

        var rssCandidates = new List<DailyNewsItemSnapshot>();
        foreach (var rssUrl in ResolveIfengNewsRssFeedUrls(normalizedChannelType))
        {
            var rssItems = await TryFetchRssNewsItemsAsync(rssUrl, candidateLimit, cancellationToken);
            if (rssItems.Count == 0)
            {
                continue;
            }

            rssCandidates = rssItems;
            break;
        }

        var htmlCandidates = await TryFetchIfengNewsItemsFromHtmlStreamAsync(
            ResolveIfengNewsListPageUrl(normalizedChannelType),
            candidateLimit,
            cancellationToken);
        var candidates = rssCandidates.Count > 0
            ? SupplementRssItemsWithHtmlFallback(rssCandidates, htmlCandidates)
            : htmlCandidates;
        if (candidates.Count == 0)
        {
            return new DailyNewsSnapshot(
                Provider: "ifeng",
                Source: ResolveIfengNewsSourceLabel(normalizedChannelType),
                Items: [],
                FetchedAt: DateTimeOffset.UtcNow);
        }

        var hydrateCount = Math.Min(candidates.Count, Math.Max(safeCount * 2, 6));
        for (var i = 0; i < hydrateCount; i++)
        {
            var candidate = candidates[i];
            if (!string.IsNullOrWhiteSpace(candidate.ImageUrl))
            {
                continue;
            }

            var coverImage = await TryFetchArticleCoverImageAsync(candidate.Url, cancellationToken);
            if (!string.IsNullOrWhiteSpace(coverImage))
            {
                candidates[i] = candidate with { ImageUrl = coverImage };
            }
        }

        var ordered = candidates
            .OrderByDescending(item => TryParseDateTimeOffset(item.PublishTime) ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(safeCount)
            .ToArray();

        return new DailyNewsSnapshot(
            Provider: "ifeng",
            Source: ResolveIfengNewsSourceLabel(normalizedChannelType),
            Items: ordered,
            FetchedAt: DateTimeOffset.UtcNow);
    }

    private async Task<List<DailyNewsItemSnapshot>> TryFetchIfengNewsItemsFromHtmlStreamAsync(
        string listPageUrl,
        int maxItems,
        CancellationToken cancellationToken)
    {
        try
        {
            var html = await FetchTextWithCnrEncodingAsync(
                listPageUrl,
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                cancellationToken);
            var streamMatch = IfengNewsStreamRegex.Match(html);
            if (!streamMatch.Success)
            {
                return [];
            }

            using var document = JsonDocument.Parse(streamMatch.Groups["json"].Value);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<DailyNewsItemSnapshot>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var limit = Math.Max(1, maxItems);
            foreach (var node in document.RootElement.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var title = NormalizeInlineText(ReadString(node, "title"));
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var link = NormalizeHttpUrl(ReadString(node, "url"));
                if (string.IsNullOrWhiteSpace(link) || !seenUrls.Add(link))
                {
                    continue;
                }

                var imageUrl = TryExtractIfengThumbnailUrl(node);
                var publishTime = NormalizeInlineText(ReadString(node, "newsTime"));

                results.Add(new DailyNewsItemSnapshot(
                    Title: title,
                    Summary: null,
                    Url: link,
                    ImageUrl: imageUrl,
                    PublishTime: string.IsNullOrWhiteSpace(publishTime) ? null : publishTime));
                if (results.Count >= limit)
                {
                    break;
                }
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    private static string? TryExtractIfengThumbnailUrl(JsonElement node)
    {
        var imagesNode = TryGetNode(node, "thumbnails", "image");
        if (imagesNode.HasValue && imagesNode.Value.ValueKind == JsonValueKind.Array)
        {
            string? candidate = null;
            foreach (var imageNode in imagesNode.Value.EnumerateArray())
            {
                if (imageNode.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var url = NormalizeHttpUrl(ReadString(imageNode, "url"));
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                candidate = url;
            }

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private IReadOnlyList<string> ResolveIfengNewsRssFeedUrls(string channelType)
    {
        var normalizedChannelType = IfengNewsChannelTypes.Normalize(channelType);
        return normalizedChannelType switch
        {
            IfengNewsChannelTypes.Mainland => _options.IfengNewsMainlandRssFeedUrls,
            IfengNewsChannelTypes.Taiwan => _options.IfengNewsTaiwanRssFeedUrls,
            _ => _options.IfengNewsComprehensiveRssFeedUrls
        };
    }

    private string ResolveIfengNewsListPageUrl(string channelType)
    {
        var normalizedChannelType = IfengNewsChannelTypes.Normalize(channelType);
        var url = normalizedChannelType switch
        {
            IfengNewsChannelTypes.Mainland => _options.IfengNewsMainlandListPageUrl,
            IfengNewsChannelTypes.Taiwan => _options.IfengNewsTaiwanListPageUrl,
            _ => _options.IfengNewsComprehensiveListPageUrl
        };

        return NormalizeHttpUrl(url)
               ?? (normalizedChannelType switch
               {
                   IfengNewsChannelTypes.Mainland => "https://news.ifeng.com/shanklist/3-35197-/",
                   IfengNewsChannelTypes.Taiwan => "https://news.ifeng.com/shanklist/3-35199-/",
                   _ => "https://news.ifeng.com/"
               });
    }

    private static string ResolveIfengNewsSourceLabel(string channelType)
    {
        return IfengNewsChannelTypes.Normalize(channelType) switch
        {
            IfengNewsChannelTypes.Mainland => "凤凰网资讯 · 中国大陆",
            IfengNewsChannelTypes.Taiwan => "凤凰网资讯 · 台湾",
            _ => "凤凰网资讯 · 综合"
        };
    }

    private bool TryGetBilibiliHotSearchFromCache(out BilibiliHotSearchSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            if (_bilibiliHotSearchCache is not null && _bilibiliHotSearchCache.ExpireAt > DateTimeOffset.UtcNow)
            {
                snapshot = _bilibiliHotSearchCache.Snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    private void SetBilibiliHotSearchCache(BilibiliHotSearchSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            _bilibiliHotSearchCache = new BilibiliHotSearchCacheEntry(
                snapshot,
                DateTimeOffset.UtcNow.Add(_options.CacheDuration));
        }
    }

    private bool TryGetBaiduHotSearchFromCache(string sourceType, out BaiduHotSearchSnapshot snapshot)
    {
        var normalizedSourceType = BaiduHotSearchSourceTypes.Normalize(sourceType);
        lock (_cacheGate)
        {
            if (_baiduHotSearchCacheBySource.TryGetValue(normalizedSourceType, out var cacheEntry) &&
                cacheEntry.ExpireAt > DateTimeOffset.UtcNow)
            {
                snapshot = cacheEntry.Snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    private void SetBaiduHotSearchCache(string sourceType, BaiduHotSearchSnapshot snapshot)
    {
        var normalizedSourceType = BaiduHotSearchSourceTypes.Normalize(sourceType);
        lock (_cacheGate)
        {
            _baiduHotSearchCacheBySource[normalizedSourceType] = new BaiduHotSearchCacheEntry(
                snapshot,
                DateTimeOffset.UtcNow.Add(_options.CacheDuration));
        }
    }

    private async Task<BaiduHotSearchSnapshot> FetchBaiduHotSearchSnapshotAsync(
        int targetCount,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var safeCount = Math.Clamp(targetCount, 1, 20);
        var normalizedSourceType = BaiduHotSearchSourceTypes.Normalize(sourceType);
        var boardUrl = NormalizeHttpUrl(_options.BaiduHotSearchBoardUrl)
            ?? "https://top.baidu.com/board?tab=realtime";

        var items = string.Equals(
            normalizedSourceType,
            BaiduHotSearchSourceTypes.ThirdPartyRss,
            StringComparison.OrdinalIgnoreCase)
            ? await FetchBaiduHotSearchItemsFromThirdPartyRssAsync(safeCount, cancellationToken)
            : await FetchBaiduHotSearchItemsFromOfficialSourceAsync(safeCount, boardUrl, cancellationToken);

        return new BaiduHotSearchSnapshot(
            Provider: "Baidu",
            Source: ResolveBaiduHotSearchSourceLabel(normalizedSourceType),
            BoardUrl: boardUrl,
            Items: items,
            FetchedAt: DateTimeOffset.UtcNow);
    }

    private async Task<IReadOnlyList<BaiduHotSearchItemSnapshot>> FetchBaiduHotSearchItemsFromOfficialSourceAsync(
        int targetCount,
        string boardUrl,
        CancellationToken cancellationToken)
    {
        var html = await FetchTextWithCnrEncodingAsync(
            boardUrl,
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            cancellationToken);

        var sDataMatch = BaiduTopBoardDataRegex.Match(html);
        if (!sDataMatch.Success)
        {
            return [];
        }

        using var document = JsonDocument.Parse(sDataMatch.Groups["json"].Value);
        var root = document.RootElement;
        var dataNode = TryGetNode(root, "data");
        if (!dataNode.HasValue || dataNode.Value.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var cardsNode = TryGetNode(dataNode.Value, "cards");
        if (!cardsNode.HasValue || cardsNode.Value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        JsonElement? hotListNode = null;
        foreach (var cardNode in cardsNode.Value.EnumerateArray())
        {
            if (cardNode.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var component = ReadString(cardNode, "component");
            if (!string.Equals(component, "hotList", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (cardNode.TryGetProperty("content", out var contentNode) &&
                contentNode.ValueKind == JsonValueKind.Array)
            {
                hotListNode = contentNode;
                break;
            }
        }

        if (!hotListNode.HasValue)
        {
            return [];
        }

        var items = new List<BaiduHotSearchItemSnapshot>(targetCount);
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var itemNode in hotListNode.Value.EnumerateArray())
        {
            if (itemNode.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = NormalizeInlineText(
                ReadString(itemNode, "word") ??
                ReadString(itemNode, "query"));
            if (string.IsNullOrWhiteSpace(title) || !seenTitles.Add(title))
            {
                continue;
            }

            var targetUrl = NormalizeHttpUrl(
                ReadString(itemNode, "rawUrl") ??
                ReadString(itemNode, "url"));
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                continue;
            }

            long? heatScore = null;
            var heatScoreText = ReadString(itemNode, "hotScore");
            if (long.TryParse(heatScoreText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedHeatScore))
            {
                heatScore = parsedHeatScore;
            }

            items.Add(new BaiduHotSearchItemSnapshot(
                Title: title,
                Url: targetUrl,
                HeatScore: heatScore));
            if (items.Count >= targetCount)
            {
                break;
            }
        }

        return items;
    }

    private async Task<IReadOnlyList<BaiduHotSearchItemSnapshot>> FetchBaiduHotSearchItemsFromThirdPartyRssAsync(
        int targetCount,
        CancellationToken cancellationToken)
    {
        var requestUrl = string.IsNullOrWhiteSpace(_options.BaiduHotSearchRssFeedUrl)
            ? "https://rss.aishort.top/?type=baidu"
            : _options.BaiduHotSearchRssFeedUrl.Trim();

        var rssItems = await TryFetchRssNewsItemsAsync(
            requestUrl,
            Math.Max(targetCount * 3, 12),
            cancellationToken);

        var items = new List<BaiduHotSearchItemSnapshot>(targetCount);
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rssItem in rssItems
                     .OrderByDescending(item => TryParseDateTimeOffset(item.PublishTime) ?? DateTimeOffset.MinValue))
        {
            var (title, heatScore) = ParseBaiduHotSearchTitle(rssItem.Title);
            if (string.IsNullOrWhiteSpace(title) || !seenTitles.Add(title))
            {
                continue;
            }

            var targetUrl = NormalizeHttpUrl(rssItem.Url);
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                continue;
            }

            items.Add(new BaiduHotSearchItemSnapshot(
                Title: title,
                Url: targetUrl,
                HeatScore: heatScore));

            if (items.Count >= targetCount)
            {
                break;
            }
        }

        return items;
    }

    private static string ResolveBaiduHotSearchSourceLabel(string sourceType)
    {
        return string.Equals(
            BaiduHotSearchSourceTypes.Normalize(sourceType),
            BaiduHotSearchSourceTypes.ThirdPartyRss,
            StringComparison.OrdinalIgnoreCase)
            ? "百度热搜 · 第三方RSS"
            : "百度热搜 · 官方";
    }

    private async Task<BilibiliHotSearchSnapshot> FetchBilibiliHotSearchSnapshotAsync(
        int targetCount,
        CancellationToken cancellationToken)
    {
        var safeCount = Math.Clamp(targetCount, 1, 20);
        var requestUrl = string.Format(
            CultureInfo.InvariantCulture,
            _options.BilibiliHotSearchApiTemplate,
            safeCount);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var responseCode = ReadString(root, "code");
        if (!string.Equals(responseCode, "0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Bilibili API returned code={responseCode ?? "unknown"}");
        }

        var listNode = TryGetNode(root, "data", "trending", "list");
        if (!listNode.HasValue || listNode.Value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Bilibili hot search list is missing.");
        }

        var items = new List<BilibiliHotSearchItemSnapshot>(safeCount);
        var seenKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var itemNode in listNode.Value.EnumerateArray())
        {
            if (itemNode.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = NormalizeInlineText(ReadString(itemNode, "show_name") ?? ReadString(itemNode, "keyword"));
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var keyword = NormalizeInlineText(ReadString(itemNode, "keyword") ?? title);
            if (string.IsNullOrWhiteSpace(keyword))
            {
                keyword = title;
            }

            if (!seenKeywords.Add(keyword))
            {
                continue;
            }

            long? heatScore = null;
            var heatScoreText = ReadString(itemNode, "heat_score");
            if (long.TryParse(heatScoreText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedHeatScore))
            {
                heatScore = parsedHeatScore;
            }

            var iconUrl = NormalizeHttpUrl(ReadString(itemNode, "icon"));
            var targetUrl = ResolveBilibiliHotSearchTargetUrl(ReadString(itemNode, "uri"), keyword);

            items.Add(new BilibiliHotSearchItemSnapshot(
                Title: title,
                Keyword: keyword,
                Url: targetUrl,
                HeatScore: heatScore,
                HasHotTag: !string.IsNullOrWhiteSpace(iconUrl),
                IconUrl: iconUrl));

            if (items.Count >= safeCount)
            {
                break;
            }
        }

        var searchPageUrl = BuildBilibiliSearchPageUrl(_options.BilibiliSearchPageUrl);
        var searchPlaceholder = await TryFetchBilibiliSearchPlaceholderAsync(cancellationToken)
            ?? items.FirstOrDefault()?.Title
            ?? "bilibili hot search";

        return new BilibiliHotSearchSnapshot(
            Provider: "Bilibili",
            Source: ReadString(root, "data", "trending", "title") ?? "bilibili热搜",
            SearchPlaceholder: searchPlaceholder,
            SearchUrl: searchPageUrl,
            MoreHotUrl: searchPageUrl,
            Items: items,
            FetchedAt: DateTimeOffset.UtcNow);
    }

    private async Task<Stcn24ForumPostsSnapshot> FetchStcn24ForumPostsSnapshotAsync(
        int targetCount,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var normalizedSourceType = Stcn24ForumSourceTypes.Normalize(sourceType);
        var isLatestCreatedSource = string.Equals(
            normalizedSourceType,
            Stcn24ForumSourceTypes.LatestCreated,
            StringComparison.OrdinalIgnoreCase);
        var safeCount = Math.Clamp(targetCount, 1, 12);
        var requestCount = Math.Clamp(Math.Max(safeCount * 3, 12), safeCount, 40);
        var keyword = NormalizeInlineText(_options.SmartTeachStcnKeyword);
        if (isLatestCreatedSource)
        {
            // For latest posts, rely on discussion id ordering from the full discussion stream.
            keyword = string.Empty;
        }
        else if (string.IsNullOrWhiteSpace(keyword))
        {
            keyword = "STCN";
        }

        var sortToken = ResolveSmartTeachDiscussionSortToken(normalizedSourceType);

        var requestUrl = string.Format(
            CultureInfo.InvariantCulture,
            _options.SmartTeachForumApiTemplate,
            Uri.EscapeDataString(keyword),
            requestCount,
            Uri.EscapeDataString(sortToken));
        requestUrl = UpsertHttpQueryParameter(requestUrl, "filter[q]", keyword);
        requestUrl = UpsertHttpQueryParameter(requestUrl, "sort", sortToken);
        requestUrl = UpsertHttpQueryParameter(
            requestUrl,
            "page[limit]",
            requestCount.ToString(CultureInfo.InvariantCulture));
        requestUrl = UpsertHttpQueryParameter(requestUrl, "include", "user");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.api+json, application/json;q=0.9, */*;q=0.8");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Forum discussion list is missing.");
        }

        var usersById = new Dictionary<string, (string? DisplayName, string? AvatarUrl)>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("included", out var includedArray) && includedArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var entity in includedArray.EnumerateArray())
            {
                if (entity.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var entityType = ReadString(entity, "type");
                if (!string.Equals(entityType, "users", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var userId = ReadString(entity, "id");
                if (string.IsNullOrWhiteSpace(userId))
                {
                    continue;
                }

                var displayName = NormalizeInlineText(
                    ReadString(entity, "attributes", "displayName") ??
                    ReadString(entity, "attributes", "username"));
                var avatarUrl = ResolveSmartTeachForumUrl(
                    ReadString(entity, "attributes", "avatarUrl"),
                    _options.SmartTeachForumBaseUrl);
                usersById[userId.Trim()] = (
                    string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                    avatarUrl);
            }
        }

        var candidates = new List<(Stcn24ForumPostItemSnapshot Item, long? DiscussionId)>(requestCount);
        foreach (var discussionNode in dataArray.EnumerateArray())
        {
            if (discussionNode.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var discussionType = ReadString(discussionNode, "type");
            if (!string.Equals(discussionType, "discussions", StringComparison.OrdinalIgnoreCase) ||
                IsSmartTeachPinnedDiscussion(discussionNode))
            {
                continue;
            }

            var discussionId = ReadString(discussionNode, "id")?.Trim();
            if (string.IsNullOrWhiteSpace(discussionId))
            {
                continue;
            }

            var title = NormalizeInlineText(ReadString(discussionNode, "attributes", "title"));
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var slug = NormalizeInlineText(ReadString(discussionNode, "attributes", "slug"));
            var shareUrl = ResolveSmartTeachForumUrl(
                ReadString(discussionNode, "attributes", "shareUrl"),
                _options.SmartTeachForumBaseUrl);
            var targetUrl = !string.IsNullOrWhiteSpace(shareUrl)
                ? shareUrl
                : BuildSmartTeachDiscussionUrl(_options.SmartTeachForumBaseUrl, discussionId, slug);
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                continue;
            }

            var authorId = ReadString(discussionNode, "relationships", "user", "data", "id");
            string? authorDisplayName = null;
            string? authorAvatarUrl = null;
            if (!string.IsNullOrWhiteSpace(authorId) &&
                usersById.TryGetValue(authorId.Trim(), out var userInfo))
            {
                authorDisplayName = userInfo.DisplayName;
                authorAvatarUrl = userInfo.AvatarUrl;
            }

            var createdAtText = ReadString(discussionNode, "attributes", "createdAt");
            var createdAt = TryParseDateTimeOffset(createdAtText);

            candidates.Add((
                new Stcn24ForumPostItemSnapshot(
                Title: title,
                Url: targetUrl,
                AuthorDisplayName: authorDisplayName,
                AuthorAvatarUrl: authorAvatarUrl,
                CreatedAt: createdAt),
                TryParseSmartTeachDiscussionId(discussionId)));
        }

        IReadOnlyList<Stcn24ForumPostItemSnapshot> items;
        if (isLatestCreatedSource)
        {
            items = candidates
                .OrderByDescending(candidate => candidate.DiscussionId ?? long.MinValue)
                .ThenByDescending(candidate => candidate.Item.CreatedAt ?? DateTimeOffset.MinValue)
                .Take(safeCount)
                .Select(candidate => candidate.Item)
                .ToArray();
        }
        else
        {
            items = candidates
                .Take(safeCount)
                .Select(candidate => candidate.Item)
                .ToArray();
        }

        return new Stcn24ForumPostsSnapshot(
            Provider: "SmartTeachForum",
            Source: ResolveStcn24ForumSourceLabel(normalizedSourceType),
            Items: items,
            FetchedAt: DateTimeOffset.UtcNow);
    }

    private static string ResolveSmartTeachDiscussionSortToken(string sourceType)
    {
        return Stcn24ForumSourceTypes.Normalize(sourceType) switch
        {
            Stcn24ForumSourceTypes.LatestCreated => "-createdAt",
            Stcn24ForumSourceTypes.LatestActivity => "-lastPostedAt",
            Stcn24ForumSourceTypes.MostReplies => "-commentCount",
            Stcn24ForumSourceTypes.EarliestCreated => "createdAt",
            Stcn24ForumSourceTypes.EarliestActivity => "lastPostedAt",
            Stcn24ForumSourceTypes.LeastReplies => "commentCount",
            Stcn24ForumSourceTypes.FrontpageLatest => "-frontdate",
            Stcn24ForumSourceTypes.FrontpageEarliest => "frontdate",
            _ => "-createdAt"
        };
    }

    private static string ResolveStcn24ForumSourceLabel(string sourceType)
    {
        return Stcn24ForumSourceTypes.Normalize(sourceType) switch
        {
            Stcn24ForumSourceTypes.LatestCreated => "智教联盟论坛 STCN · 最新发布",
            Stcn24ForumSourceTypes.LatestActivity => "智教联盟论坛 STCN · 最新回复",
            Stcn24ForumSourceTypes.MostReplies => "智教联盟论坛 STCN · 回复最多",
            Stcn24ForumSourceTypes.EarliestCreated => "智教联盟论坛 STCN · 最早发布",
            Stcn24ForumSourceTypes.EarliestActivity => "智教联盟论坛 STCN · 最早回复",
            Stcn24ForumSourceTypes.LeastReplies => "智教联盟论坛 STCN · 回复最少",
            Stcn24ForumSourceTypes.FrontpageLatest => "智教联盟论坛 STCN · 前台推荐（新）",
            Stcn24ForumSourceTypes.FrontpageEarliest => "智教联盟论坛 STCN · 前台推荐（旧）",
            _ => "智教联盟论坛 STCN · 最新发布"
        };
    }

    private static string UpsertHttpQueryParameter(string requestUrl, string key, string value)
    {
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var uri))
        {
            return requestUrl;
        }

        var parameters = new List<(string Key, string Value)>();
        var replaced = false;
        var query = uri.Query.TrimStart('?');
        if (!string.IsNullOrWhiteSpace(query))
        {
            var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var separatorIndex = part.IndexOf('=');
                var rawKey = separatorIndex >= 0 ? part[..separatorIndex] : part;
                var rawValue = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : string.Empty;
                var normalizedKey = Uri.UnescapeDataString(rawKey);
                if (string.Equals(normalizedKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (!replaced)
                    {
                        parameters.Add((key, value));
                        replaced = true;
                    }

                    continue;
                }

                parameters.Add((normalizedKey, Uri.UnescapeDataString(rawValue)));
            }
        }

        if (!replaced)
        {
            parameters.Add((key, value));
        }

        var rebuiltQuery = string.Join(
            "&",
            parameters.Select(item =>
                $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));

        var builder = new UriBuilder(uri)
        {
            Query = rebuiltQuery
        };
        return builder.Uri.ToString();
    }

    private async Task<string?> TryFetchBilibiliSearchPlaceholderAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BilibiliSearchDefaultApiUrl))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.BilibiliSearchDefaultApiUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (!string.Equals(ReadString(root, "code"), "0", StringComparison.Ordinal))
            {
                return null;
            }

            var placeholder = NormalizeInlineText(
                ReadString(root, "data", "show_name") ??
                ReadString(root, "data", "name"));

            return string.IsNullOrWhiteSpace(placeholder)
                ? null
                : placeholder;
        }
        catch
        {
            return null;
        }
    }

    private string ResolveBilibiliHotSearchTargetUrl(string? rawUri, string keyword)
    {
        var normalizedDirectUrl = NormalizeHttpUrl(rawUri);
        if (!string.IsNullOrWhiteSpace(normalizedDirectUrl))
        {
            return normalizedDirectUrl;
        }

        return BuildBilibiliSearchUrl(_options.BilibiliSearchPageUrl, keyword);
    }

    private static string BuildBilibiliSearchPageUrl(string? baseSearchUrl)
    {
        var fallback = "https://search.bilibili.com/all";
        var candidate = string.IsNullOrWhiteSpace(baseSearchUrl)
            ? fallback
            : baseSearchUrl.Trim();
        var normalized = NormalizeHttpUrl(candidate);
        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized;
    }

    private static string BuildBilibiliSearchUrl(string? baseSearchUrl, string keyword)
    {
        var searchPage = BuildBilibiliSearchPageUrl(baseSearchUrl);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return searchPage;
        }

        var separator = searchPage.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{searchPage}{separator}keyword={Uri.EscapeDataString(keyword)}";
    }

    private IReadOnlyList<DailyNewsItemSnapshot> SelectDailyNewsItems(
        IReadOnlyList<DailyNewsItemSnapshot> items,
        int targetCount,
        bool forceRefresh)
    {
        if (items.Count == 0 || targetCount <= 0)
        {
            return [];
        }

        var safeCount = Math.Min(targetCount, items.Count);
        if (!forceRefresh || items.Count <= safeCount)
        {
            return items.Take(safeCount).ToArray();
        }

        var cursor = Math.Abs(Interlocked.Increment(ref _dailyNewsRotationCursor) - 1);
        var startIndex = cursor % items.Count;
        var selection = new List<DailyNewsItemSnapshot>(safeCount);
        for (var i = 0; i < safeCount; i++)
        {
            selection.Add(items[(startIndex + i) % items.Count]);
        }

        return selection;
    }

    private bool TryGetStcn24ForumPostsFromCache(string sourceType, out Stcn24ForumPostsSnapshot snapshot)
    {
        var normalizedSourceType = Stcn24ForumSourceTypes.Normalize(sourceType);
        lock (_cacheGate)
        {
            if (_stcn24ForumPostsCacheBySource.TryGetValue(normalizedSourceType, out var cacheEntry) &&
                cacheEntry.ExpireAt > DateTimeOffset.UtcNow)
            {
                snapshot = cacheEntry.Snapshot;
                return true;
            }

            _stcn24ForumPostsCacheBySource.Remove(normalizedSourceType);
        }

        snapshot = null!;
        return false;
    }

    private void SetStcn24ForumPostsCache(string sourceType, Stcn24ForumPostsSnapshot snapshot)
    {
        var normalizedSourceType = Stcn24ForumSourceTypes.Normalize(sourceType);
        lock (_cacheGate)
        {
            _stcn24ForumPostsCacheBySource[normalizedSourceType] = new Stcn24ForumPostsCacheEntry(
                snapshot,
                DateTimeOffset.UtcNow.Add(_options.CacheDuration));
        }
    }

    private bool TryGetDailyWordFromCache(out DailyWordSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            if (_dailyWordCache is not null && _dailyWordCache.ExpireAt > DateTimeOffset.UtcNow)
            {
                snapshot = _dailyWordCache.Snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    private void SetDailyWordCache(DailyWordSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            _dailyWordCache = new DailyWordCacheEntry(
                snapshot,
                DateTimeOffset.UtcNow.Add(_options.CacheDuration));
        }
    }

    private bool TryGetExchangeRateTableFromCache(string baseCurrency, out ExchangeRateTableCacheEntry entry)
    {
        lock (_cacheGate)
        {
            if (_exchangeRateCacheByBaseCurrency.TryGetValue(baseCurrency, out var cached) &&
                cached.ExpireAt > DateTimeOffset.UtcNow)
            {
                entry = cached;
                return true;
            }

            _exchangeRateCacheByBaseCurrency.Remove(baseCurrency);
        }

        entry = null!;
        return false;
    }

    private void SetExchangeRateTableCache(string baseCurrency, ExchangeRateTableCacheEntry entry)
    {
        lock (_cacheGate)
        {
            _exchangeRateCacheByBaseCurrency[baseCurrency] = entry;
        }
    }

    private async Task<ExchangeRateSnapshot> FetchExchangeRateSnapshotAsync(
        string baseCurrency,
        string targetCurrency,
        CancellationToken cancellationToken)
    {
        var requestUrl = string.Format(
            CultureInfo.InvariantCulture,
            _options.ExchangeRateApiTemplate,
            Uri.EscapeDataString(baseCurrency));
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        if (!root.TryGetProperty("rates", out var ratesNode) || ratesNode.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Exchange rate payload is missing rates.");
        }

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [baseCurrency] = 1m
        };
        foreach (var property in ratesNode.EnumerateObject())
        {
            var currency = NormalizeCurrencyCode(property.Name, string.Empty);
            if (string.IsNullOrWhiteSpace(currency))
            {
                continue;
            }

            if (TryReadDecimalValue(property.Value, out var value) && value > 0)
            {
                rates[currency] = value;
            }
        }

        if (!rates.TryGetValue(targetCurrency, out var rate) || rate <= 0)
        {
            throw new InvalidOperationException($"Currency {targetCurrency} is not provided by upstream.");
        }

        var fetchedAt = DateTimeOffset.UtcNow;
        var cacheEntry = new ExchangeRateTableCacheEntry(
            baseCurrency,
            rates,
            fetchedAt.Add(_options.CacheDuration),
            fetchedAt);
        SetExchangeRateTableCache(baseCurrency, cacheEntry);
        return new ExchangeRateSnapshot(
            Provider: "open.er-api.com",
            Source: "open.er-api.com",
            BaseCurrency: baseCurrency,
            TargetCurrency: targetCurrency,
            Rate: rate,
            FetchedAt: fetchedAt);
    }

    private static string NormalizeCurrencyCode(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length < 3)
        {
            return fallback;
        }

        return normalized[..3];
    }

    private static bool TryReadDecimalValue(JsonElement element, out decimal value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetDecimal(out value))
                {
                    return true;
                }

                if (element.TryGetDouble(out var numeric))
                {
                    value = (decimal)numeric;
                    return true;
                }

                break;
            case JsonValueKind.String:
                if (decimal.TryParse(
                    element.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value))
                {
                    return true;
                }

                break;
        }

        value = 0m;
        return false;
    }

    private List<string> BuildDailyWordCandidates()
    {
        var values = _options.YoudaoDailyWordCandidates ?? [];
        var result = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawValue in values)
        {
            var normalized = NormalizeDailyWordCandidate(rawValue);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    private static string? NormalizeDailyWordCandidate(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var compact = Regex.Replace(rawValue.Trim(), "\\s+", string.Empty);
        if (compact.Length < 2 || compact.Length > 48)
        {
            return null;
        }

        foreach (var ch in compact)
        {
            if (char.IsLetter(ch) || ch == '-' || ch == '\'')
            {
                continue;
            }

            return null;
        }

        return compact.ToLowerInvariant();
    }

    private static int ResolveDailyWordStartIndex(int candidateCount, bool forceRefresh)
    {
        if (candidateCount <= 0)
        {
            return 0;
        }

        if (forceRefresh)
        {
            return Random.Shared.Next(candidateCount);
        }

        var localDate = GetChinaLocalDate();
        var seed = localDate.Year * 1000 + localDate.DayOfYear;
        return Math.Abs(seed) % candidateCount;
    }

    private async Task<DailyWordSnapshot?> TryFetchYoudaoDailyWordAsync(string candidateWord, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidateWord))
        {
            return null;
        }

        var requestUrl = string.Format(
            CultureInfo.InvariantCulture,
            _options.YoudaoDictionaryApiTemplate,
            Uri.EscapeDataString(candidateWord.Trim()));

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var word = ResolveYoudaoWord(root, candidateWord);
        if (string.IsNullOrWhiteSpace(word))
        {
            return null;
        }

        var meaning = ExtractYoudaoMeaning(root);
        if (string.IsNullOrWhiteSpace(meaning))
        {
            return null;
        }

        var (exampleSentence, exampleTranslation) = ExtractYoudaoExample(root);
        var (ukPhone, usPhone) = ExtractYoudaoPronunciations(root);
        return new DailyWordSnapshot(
            Provider: "YoudaoDictionary",
            Word: word,
            UkPronunciation: ukPhone,
            UsPronunciation: usPhone,
            Meaning: meaning,
            ExampleSentence: exampleSentence,
            ExampleTranslation: exampleTranslation,
            SourceUrl: BuildYoudaoWordPageUrl(word),
            FetchedAt: DateTimeOffset.UtcNow);
    }

    private static string? ResolveYoudaoWord(JsonElement root, string fallbackWord)
    {
        var candidate =
            ReadString(root, "simple", "query") ??
            ReadString(root, "meta", "input") ??
            ReadString(root, "input") ??
            fallbackWord;
        return NormalizeDailyWordCandidate(candidate);
    }

    private static (string? UkPhone, string? UsPhone) ExtractYoudaoPronunciations(JsonElement root)
    {
        var simpleWord = TryGetFirstArrayObject(root, "simple", "word");
        if (!simpleWord.HasValue)
        {
            simpleWord = TryGetFirstArrayObject(root, "ec", "word");
        }

        if (!simpleWord.HasValue)
        {
            return (null, null);
        }

        var ukPhone = NormalizePhoneText(ReadString(simpleWord.Value, "ukphone"));
        var usPhone = NormalizePhoneText(ReadString(simpleWord.Value, "usphone"));
        return (ukPhone, usPhone);
    }

    private static string? ExtractYoudaoMeaning(JsonElement root)
    {
        var ecWord = TryGetFirstArrayObject(root, "ec", "word");
        if (!ecWord.HasValue || !ecWord.Value.TryGetProperty("trs", out var trsNode) || trsNode.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var lines = new List<string>();
        foreach (var trNode in trsNode.EnumerateArray())
        {
            if (trNode.ValueKind != JsonValueKind.Object ||
                !trNode.TryGetProperty("tr", out var trArray) ||
                trArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var trItem in trArray.EnumerateArray())
            {
                var line = ExtractYoudaoMeaningLine(trItem);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }
        }

        if (lines.Count == 0)
        {
            return null;
        }

        return string.Join("; ", lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3));
    }

    private static string? ExtractYoudaoMeaningLine(JsonElement trItem)
    {
        if (trItem.ValueKind == JsonValueKind.String)
        {
            return NormalizeInlineText(trItem.GetString());
        }

        if (trItem.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (trItem.TryGetProperty("l", out var languageNode))
        {
            if (languageNode.ValueKind == JsonValueKind.Object &&
                languageNode.TryGetProperty("i", out var textNode))
            {
                if (textNode.ValueKind == JsonValueKind.Array)
                {
                    var fragments = textNode.EnumerateArray()
                        .Select(item => item.ValueKind == JsonValueKind.String ? NormalizeInlineText(item.GetString()) : null)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .ToArray();
                    if (fragments.Length > 0)
                    {
                        return string.Join(" ", fragments);
                    }
                }
                else if (textNode.ValueKind == JsonValueKind.String)
                {
                    return NormalizeInlineText(textNode.GetString());
                }
            }
            else if (languageNode.ValueKind == JsonValueKind.String)
            {
                return NormalizeInlineText(languageNode.GetString());
            }
        }

        return null;
    }

    private static (string? Sentence, string? Translation) ExtractYoudaoExample(JsonElement root)
    {
        if (!root.TryGetProperty("blng_sents_part", out var sentencePartNode) ||
            sentencePartNode.ValueKind != JsonValueKind.Object ||
            !sentencePartNode.TryGetProperty("sentence-pair", out var sentencePairNode) ||
            sentencePairNode.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        foreach (var sentenceNode in sentencePairNode.EnumerateArray())
        {
            if (sentenceNode.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var sentence = NormalizeInlineText(
                ReadString(sentenceNode, "sentence") ??
                ReadString(sentenceNode, "sentence-eng"));
            if (string.IsNullOrWhiteSpace(sentence))
            {
                continue;
            }

            var translation = NormalizeInlineText(ReadString(sentenceNode, "sentence-translation"));
            return (sentence, translation);
        }

        return (null, null);
    }

    private string? BuildYoudaoWordPageUrl(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return null;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            _options.YoudaoDictionaryWordPageTemplate,
            Uri.EscapeDataString(word.Trim()));
    }

    private static string? NormalizePhoneText(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var compact = Regex.Replace(phone.Trim(), "\\s+", string.Empty);
        return compact.Length == 0 ? null : compact;
    }

    private static JsonElement? TryGetFirstArrayObject(JsonElement node, params string[] path)
    {
        var arrayNode = TryGetNode(node, path);
        if (!arrayNode.HasValue || arrayNode.Value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in arrayNode.Value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                return item;
            }
        }

        return null;
    }

    private async Task<List<DailyNewsItemSnapshot>> FetchCnrDailyNewsItemsAsync(
        int requestedItemCount,
        CancellationToken cancellationToken)
    {
        var requestUrl = string.IsNullOrWhiteSpace(_options.CnrDailyNewsListUrl)
            ? "https://www.cnr.cn/newscenter/native/gd/"
            : _options.CnrDailyNewsListUrl.Trim();
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var listPageUri))
        {
            throw new InvalidOperationException("CNR news list URL is invalid.");
        }

        var html = await FetchHtmlWithCnrEncodingAsync(requestUrl, cancellationToken);
        var targetCount = Math.Clamp(requestedItemCount, 1, 12);
        var candidateLimit = Math.Max(8, targetCount * 3);
        var htmlCandidates = ParseCnrDailyNewsFromListPage(
            html,
            listPageUri,
            candidateLimit).ToList();

        var rssCandidates = new List<DailyNewsItemSnapshot>();
        var rssCandidateUrls = BuildCnrDailyNewsRssCandidateUrls(listPageUri, html);
        foreach (var rssUrl in rssCandidateUrls)
        {
            var rssItems = await TryFetchRssNewsItemsAsync(rssUrl, candidateLimit, cancellationToken);
            if (rssItems.Count == 0)
            {
                continue;
            }

            rssCandidates = rssItems;
            break;
        }

        var candidates = rssCandidates.Count > 0
            ? SupplementRssItemsWithHtmlFallback(rssCandidates, htmlCandidates)
            : htmlCandidates;
        if (candidates.Count == 0)
        {
            return [];
        }

        var hydrateCount = Math.Min(candidates.Count, Math.Max(targetCount * 2, 4));
        for (var i = 0; i < hydrateCount; i++)
        {
            var candidate = candidates[i];
            if (!string.IsNullOrWhiteSpace(candidate.ImageUrl))
            {
                continue;
            }

            var coverImage = await TryFetchArticleCoverImageAsync(candidate.Url, cancellationToken);
            if (!string.IsNullOrWhiteSpace(coverImage))
            {
                candidates[i] = candidate with { ImageUrl = coverImage };
            }
        }

        return candidates;
    }

    private List<string> BuildCnrDailyNewsRssCandidateUrls(Uri listPageUri, string listPageHtml)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_options.CnrDailyNewsRssFeedUrls is { Count: > 0 })
        {
            foreach (var configured in _options.CnrDailyNewsRssFeedUrls)
            {
                var normalized = ResolveAbsoluteUrl(configured, listPageUri);
                if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
                {
                    results.Add(normalized);
                }
            }
        }

        foreach (Match match in RssAlternateLinkRegex.Matches(listPageHtml))
        {
            var normalized = ResolveAbsoluteUrl(match.Groups["url"].Value, listPageUri);
            if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
            {
                results.Add(normalized);
            }
        }

        var fallbackGuesses = new[]
        {
            "https://www.cnr.cn/rss.xml",
            "https://news.cnr.cn/rss.xml",
            "https://www.cnr.cn/newscenter/native/gd/rss.xml",
            "https://news.cnr.cn/native/gd/rss.xml"
        };
        foreach (var guess in fallbackGuesses)
        {
            if (seen.Add(guess))
            {
                results.Add(guess);
            }
        }

        return results;
    }

    private async Task<List<DailyNewsItemSnapshot>> TryFetchRssNewsItemsAsync(
        string rssUrl,
        int maxItems,
        CancellationToken cancellationToken)
    {
        try
        {
            var xml = await FetchTextWithCnrEncodingAsync(
                rssUrl,
                "application/rss+xml,application/atom+xml,text/xml,application/xml;q=0.9,*/*;q=0.8",
                cancellationToken);

            var document = XDocument.Parse(xml, LoadOptions.None);
            if (!Uri.TryCreate(rssUrl, UriKind.Absolute, out var feedUri))
            {
                return [];
            }

            var results = new List<DailyNewsItemSnapshot>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var itemLimit = Math.Max(1, maxItems);
            foreach (var node in document.Descendants())
            {
                var localName = node.Name.LocalName;
                if (!string.Equals(localName, "item", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(localName, "entry", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var link = ResolveAbsoluteUrl(ExtractRssItemLink(node), feedUri);
                var normalizedLinkKey = NormalizeNewsUrlKey(link);
                if (string.IsNullOrWhiteSpace(link) ||
                    string.IsNullOrWhiteSpace(normalizedLinkKey) ||
                    !seenUrls.Add(normalizedLinkKey))
                {
                    continue;
                }

                var title = NormalizeInlineText(ExtractFirstElementValue(node, "title"));
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var summarySource = ExtractFirstElementValue(node, "description")
                    ?? ExtractFirstElementValue(node, "summary")
                    ?? ExtractFirstElementValue(node, "encoded")
                    ?? string.Empty;
                var summary = NormalizeInlineText(summarySource);
                var publishTime = NormalizeInlineText(
                    ExtractFirstElementValue(node, "pubDate")
                    ?? ExtractFirstElementValue(node, "updated")
                    ?? ExtractFirstElementValue(node, "published")
                    ?? string.Empty);
                var imageUrl = ExtractRssItemImageUrl(node, feedUri, summarySource);

                results.Add(new DailyNewsItemSnapshot(
                    Title: title,
                    Summary: string.IsNullOrWhiteSpace(summary) ? null : summary,
                    Url: link,
                    ImageUrl: imageUrl,
                    PublishTime: string.IsNullOrWhiteSpace(publishTime) ? null : publishTime));
                if (results.Count >= itemLimit)
                {
                    break;
                }
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    private static string? ExtractRssItemLink(XElement itemNode)
    {
        var linkElement = itemNode.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase));
        if (linkElement is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(linkElement.Value))
        {
            return linkElement.Value.Trim();
        }

        var href = linkElement.Attribute("href")?.Value;
        return string.IsNullOrWhiteSpace(href) ? null : href.Trim();
    }

    private static string? ExtractFirstElementValue(XElement itemNode, string localName)
    {
        var element = itemNode.Elements()
            .FirstOrDefault(node => string.Equals(node.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        return element?.Value;
    }

    private static string? ExtractRssItemImageUrl(XElement itemNode, Uri feedUri, string descriptionHtml)
    {
        foreach (var element in itemNode.Elements())
        {
            var name = element.Name.LocalName;
            if (string.Equals(name, "enclosure", StringComparison.OrdinalIgnoreCase))
            {
                var type = element.Attribute("type")?.Value ?? string.Empty;
                var url = ResolveAbsoluteUrl(element.Attribute("url")?.Value, feedUri);
                if (!string.IsNullOrWhiteSpace(url) &&
                    (type.Contains("image", StringComparison.OrdinalIgnoreCase) || IsLikelyContentImageUrl(url)))
                {
                    return url;
                }
            }

            if (string.Equals(name, "content", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "thumbnail", StringComparison.OrdinalIgnoreCase))
            {
                var url = ResolveAbsoluteUrl(element.Attribute("url")?.Value, feedUri);
                if (IsLikelyContentImageUrl(url))
                {
                    return url;
                }
            }
        }

        foreach (Match match in RssDescriptionImageRegex.Matches(descriptionHtml ?? string.Empty))
        {
            var url = ResolveAbsoluteUrl(match.Groups["url"].Value, feedUri);
            if (IsLikelyContentImageUrl(url))
            {
                return url;
            }
        }

        return null;
    }

    private static List<DailyNewsItemSnapshot> SupplementRssItemsWithHtmlFallback(
        IReadOnlyList<DailyNewsItemSnapshot> rssItems,
        IReadOnlyList<DailyNewsItemSnapshot> htmlItems)
    {
        if (rssItems.Count == 0)
        {
            return htmlItems.ToList();
        }

        if (htmlItems.Count == 0)
        {
            return rssItems.ToList();
        }

        var htmlByUrl = htmlItems
            .Select(item => (key: NormalizeNewsUrlKey(item.Url), item))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.key))
            .GroupBy(pair => pair.key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().item, StringComparer.OrdinalIgnoreCase);

        var merged = new List<DailyNewsItemSnapshot>(rssItems.Count);
        foreach (var rssItem in rssItems)
        {
            var key = NormalizeNewsUrlKey(rssItem.Url);
            if (!string.IsNullOrWhiteSpace(key) && htmlByUrl.TryGetValue(key, out var htmlItem))
            {
                merged.Add(rssItem with
                {
                    Summary = string.IsNullOrWhiteSpace(rssItem.Summary) ? htmlItem.Summary : rssItem.Summary,
                    ImageUrl = string.IsNullOrWhiteSpace(rssItem.ImageUrl) ? htmlItem.ImageUrl : rssItem.ImageUrl
                });
            }
            else
            {
                merged.Add(rssItem);
            }
        }

        return merged;
    }

    private static string? NormalizeNewsUrlKey(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.ToString().TrimEnd('/');
    }

    private async Task<string> FetchHtmlWithCnrEncodingAsync(string requestUrl, CancellationToken cancellationToken)
    {
        return await FetchTextWithCnrEncodingAsync(
            requestUrl,
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            cancellationToken);
    }

    private async Task<string> FetchTextWithCnrEncodingAsync(
        string requestUrl,
        string acceptHeader,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", acceptHeader);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var decodedText = DecodeHttpPayload(payload, response.Content.Headers.ContentType?.CharSet);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(decodedText, 180)}");
        }

        return decodedText;
    }

    private static string DecodeHttpPayload(byte[] payload, string? declaredCharset)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        if (TryGetTextEncoding(declaredCharset, out var declaredEncoding))
        {
            return declaredEncoding.GetString(payload);
        }

        var utf8Text = Encoding.UTF8.GetString(payload);
        var charsetFromMeta = ExtractCharsetFromHtmlMeta(utf8Text);
        if (TryGetTextEncoding(charsetFromMeta, out var metaEncoding))
        {
            return metaEncoding.GetString(payload);
        }

        return utf8Text;
    }

    private static string? ExtractCharsetFromHtmlMeta(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var match = Regex.Match(
            html,
            "charset\\s*=\\s*[\"']?(?<value>[A-Za-z0-9_\\-]+)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static bool TryGetTextEncoding(string? charset, out Encoding encoding)
    {
        encoding = Encoding.UTF8;
        if (string.IsNullOrWhiteSpace(charset))
        {
            return false;
        }

        var normalized = charset.Trim().Trim('"', '\'').ToLowerInvariant();
        if (normalized is "gb2312" or "gbk" or "cp936")
        {
            normalized = "gb18030";
        }

        try
        {
            encoding = Encoding.GetEncoding(normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<DailyNewsItemSnapshot> ParseCnrDailyNewsFromListPage(
        string html,
        Uri listPageUri,
        int maxItems)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            yield break;
        }

        var startIndex = html.IndexOf("<div class=\"articleList\"", StringComparison.OrdinalIgnoreCase);
        var scope = startIndex >= 0 ? html[startIndex..] : html;
        var maxCount = Math.Max(1, maxItems);
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in CnrListAnchorRegex.Matches(scope))
        {
            var normalizedUrl = ResolveAbsoluteUrl(match.Groups["url"].Value, listPageUri);
            if (string.IsNullOrWhiteSpace(normalizedUrl) || !seenUrls.Add(normalizedUrl))
            {
                continue;
            }

            var inner = match.Groups["inner"].Value;
            var title = ExtractTagInnerText(inner, "strong");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var summary = ExtractTagInnerText(inner, "em");
            var publishTime = ExtractTagInnerTextByClass(inner, "span", "publishTime");
            var imageUrl = ExtractFirstImageUrl(inner, listPageUri);
            yield return new DailyNewsItemSnapshot(
                Title: title,
                Summary: summary,
                Url: normalizedUrl,
                ImageUrl: imageUrl,
                PublishTime: publishTime);

            if (seenUrls.Count >= maxCount)
            {
                yield break;
            }
        }
    }

    private static string? ExtractTagInnerText(string htmlFragment, string tagName)
    {
        if (string.IsNullOrWhiteSpace(htmlFragment) || string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var match = Regex.Match(
            htmlFragment,
            $"<{tagName}[^>]*>(?<value>.*?)</{tagName}>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
        {
            return null;
        }

        return NormalizeInlineText(match.Groups["value"].Value);
    }

    private static string? ExtractTagInnerTextByClass(string htmlFragment, string tagName, string className)
    {
        if (string.IsNullOrWhiteSpace(htmlFragment) ||
            string.IsNullOrWhiteSpace(tagName) ||
            string.IsNullOrWhiteSpace(className))
        {
            return null;
        }

        var match = Regex.Match(
            htmlFragment,
            $"<{tagName}[^>]*class=\"[^\"]*{Regex.Escape(className)}[^\"]*\"[^>]*>(?<value>.*?)</{tagName}>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
        {
            return null;
        }

        return NormalizeInlineText(match.Groups["value"].Value);
    }

    private static string? ExtractFirstImageUrl(string htmlFragment, Uri pageUri)
    {
        if (string.IsNullOrWhiteSpace(htmlFragment))
        {
            return null;
        }

        var matches = HtmlImageTagRegex.Matches(htmlFragment);
        foreach (Match match in matches)
        {
            var normalized = ResolveAbsoluteUrl(match.Groups["url"].Value, pageUri);
            if (IsLikelyContentImageUrl(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private async Task<string?> TryFetchArticleCoverImageAsync(string articleUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(articleUrl, UriKind.Absolute, out var articleUri))
        {
            return null;
        }

        var html = await FetchHtmlWithCnrEncodingAsync(articleUrl, cancellationToken);

        var metaMatches = new[]
        {
            Regex.Match(
                html,
                "<meta[^>]+property=\"og:image\"[^>]+content=\"(?<url>[^\"]+)\"",
                RegexOptions.IgnoreCase),
            Regex.Match(
                html,
                "<meta[^>]+name=\"image\"[^>]+content=\"(?<url>[^\"]+)\"",
                RegexOptions.IgnoreCase)
        };

        foreach (var metaMatch in metaMatches)
        {
            if (!metaMatch.Success)
            {
                continue;
            }

            var metaUrl = ResolveAbsoluteUrl(metaMatch.Groups["url"].Value, articleUri);
            if (IsLikelyContentImageUrl(metaUrl))
            {
                return metaUrl;
            }
        }

        var imageMatches = Regex.Matches(
            html,
            "<img[^>]+src=\"(?<url>[^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match imageMatch in imageMatches)
        {
            var normalized = ResolveAbsoluteUrl(imageMatch.Groups["url"].Value, articleUri);
            if (IsLikelyContentImageUrl(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static bool IsLikelyContentImageUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return false;
        }

        var value = imageUrl.Trim();
        if (!(value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
              value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
              value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
              value.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
              value.EndsWith(".avif", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !(value.Contains("share", StringComparison.OrdinalIgnoreCase) ||
                 value.Contains("logo", StringComparison.OrdinalIgnoreCase) ||
                 value.Contains("code.png", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveAbsoluteUrl(string? rawUrl, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var candidate = rawUrl.Trim();
        if (candidate.Contains("'+", StringComparison.Ordinal) ||
            candidate.Contains("+'", StringComparison.Ordinal))
        {
            return null;
        }

        if (candidate.StartsWith("//", StringComparison.Ordinal))
        {
            return $"{baseUri.Scheme}:{candidate}";
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
        {
            if (!string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return absoluteUri.ToString();
        }

        return Uri.TryCreate(baseUri, candidate, out var relativeUri)
            ? relativeUri.ToString()
            : null;
    }

    private static bool IsSmartTeachPinnedDiscussion(JsonElement discussionNode)
    {
        return ReadBoolean(discussionNode, "attributes", "isStickiest") ||
               ReadBoolean(discussionNode, "attributes", "isSticky") ||
               ReadBoolean(discussionNode, "attributes", "isTagSticky") ||
               ReadBoolean(discussionNode, "attributes", "front") ||
               ReadBoolean(discussionNode, "attributes", "frontpage");
    }

    private static string? ResolveSmartTeachForumUrl(string? rawUrl, string? baseUrl)
    {
        var normalizedAbsolute = NormalizeHttpUrl(rawUrl);
        if (!string.IsNullOrWhiteSpace(normalizedAbsolute))
        {
            return normalizedAbsolute;
        }

        if (string.IsNullOrWhiteSpace(rawUrl) || string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        var normalized = ResolveAbsoluteUrl(rawUrl, baseUri);
        return NormalizeHttpUrl(normalized);
    }

    private static string? BuildSmartTeachDiscussionUrl(string? baseUrl, string discussionId, string slug)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(discussionId))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        var normalizedId = discussionId.Trim();
        var normalizedSlug = slug.Trim();
        string path;
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            path = $"/d/{normalizedId}";
        }
        else if (normalizedSlug.StartsWith($"{normalizedId}-", StringComparison.OrdinalIgnoreCase))
        {
            path = $"/d/{normalizedSlug}";
        }
        else
        {
            path = $"/d/{normalizedId}-{normalizedSlug}";
        }

        return new Uri(baseUri, path).ToString();
    }

    private static DateTimeOffset? TryParseDateTimeOffset(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            rawValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }

    private static long? TryParseSmartTeachDiscussionId(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return long.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static (string Title, long? HeatScore) ParseBaiduHotSearchTitle(string? rawTitle)
    {
        var normalized = NormalizeInlineText(rawTitle);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return (string.Empty, null);
        }

        var match = BaiduHotSearchHeatRegex.Match(normalized);
        if (!match.Success)
        {
            return (normalized, null);
        }

        var title = NormalizeInlineText(match.Groups["keyword"].Value);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = normalized;
        }

        var heatScoreText = match.Groups["heat"].Value;
        if (long.TryParse(heatScoreText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var heatScore))
        {
            return (title, heatScore);
        }

        return (title, null);
    }

    private static string NormalizeInlineText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decoded = WebUtility.HtmlDecode(text);
        var withoutTags = HtmlTagRegex.Replace(decoded ?? string.Empty, " ");
        return Regex.Replace(withoutTags, "\\s+", " ").Trim();
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

    private static bool ReadBoolean(JsonElement node, params string[] path)
    {
        var target = TryGetNode(node, path);
        if (!target.HasValue)
        {
            return false;
        }

        var value = target.Value;
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                return bool.TryParse(value.GetString(), out var boolValue) && boolValue;
            case JsonValueKind.Number:
                return value.TryGetInt32(out var intValue) && intValue != 0;
            default:
                return false;
        }
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
            var snapshot = _componentSettingsService.Load();
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

    private static string? NormalizeHttpUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var candidate = rawUrl.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.ToString();
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

    // 智教Hub相关方法
    public async Task<RecommendationQueryResult<ZhiJiaoHubSnapshot>> GetZhiJiaoHubImagesAsync(
        ZhiJiaoHubQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query ?? new ZhiJiaoHubQuery();
        var source = ZhiJiaoHubSources.Normalize(normalizedQuery.Source);
        var mirrorSource = ZhiJiaoHubMirrorSources.Normalize(normalizedQuery.MirrorSource);
        var cacheKey = $"{source}|{mirrorSource}";

        if (!normalizedQuery.ForceRefresh && TryGetZhiJiaoHubFromCache(cacheKey, out var cached))
        {
            return RecommendationQueryResult<ZhiJiaoHubSnapshot>.Ok(cached);
        }

        try
        {
            var snapshot = await FetchZhiJiaoHubSnapshotAsync(source, mirrorSource, cancellationToken);
            SetZhiJiaoHubCache(cacheKey, snapshot);
            return RecommendationQueryResult<ZhiJiaoHubSnapshot>.Ok(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return RecommendationQueryResult<ZhiJiaoHubSnapshot>.Fail("upstream_network_error", ex.Message);
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<ZhiJiaoHubSnapshot>.Fail("upstream_parse_error", ex.Message);
        }
    }

    private async Task<ZhiJiaoHubSnapshot> FetchZhiJiaoHubSnapshotAsync(string source, string mirrorSource, CancellationToken cancellationToken)
    {
        var (owner, repo, path) = source switch
        {
            ZhiJiaoHubSources.Sectl => ("SECTL", "SECTL-hub", "docs/.vuepress/public/images"),
            ZhiJiaoHubSources.RinLit => ("RinLit-233-shiroko", "Rin-sHub", "images"),
            _ => ("ClassIsland", "classisland-hub", "images")
        };

        var contentsUrl = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";
        
        // 如果使用镜像加速，代理 GitHub API 请求
        if (string.Equals(mirrorSource, ZhiJiaoHubMirrorSources.GhProxy, StringComparison.OrdinalIgnoreCase))
        {
            contentsUrl = ZhiJiaoHubMirrorSources.GhProxyBaseUrl.TrimEnd('/') + "/" + contentsUrl;
        }

        try
        {
            var images = await FetchImagesFromContentsApi(owner, repo, path, contentsUrl, mirrorSource, cancellationToken);

            if (images.Count == 0)
            {
                throw new InvalidOperationException("未找到图片文件");
            }

            // 随机打乱图片顺序
            var random = new Random();
            var shuffled = images.OrderBy(_ => random.Next()).ToList();

            // 重新设置索引
            for (int i = 0; i < shuffled.Count; i++)
            {
                var item = shuffled[i];
                shuffled[i] = item with { Index = i };
            }

            return new ZhiJiaoHubSnapshot(shuffled, 0, source);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("403") || ex.Message.Contains("rate limit"))
        {
            throw new HttpRequestException("GitHub API 速率限制，请稍后重试");
        }
        catch (Exception ex)
        {
            throw new HttpRequestException($"获取图片列表失败: {ex.Message}");
        }
    }

    private async Task<List<ZhiJiaoHubImageItem>> FetchImagesFromContentsApi(string owner, string repo, string path, string contentsUrl, string mirrorSource, CancellationToken cancellationToken)
    {
        var images = new List<ZhiJiaoHubImageItem>();

        using var request = new HttpRequestMessage(HttpMethod.Get, contentsUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", "LanMountainDesktop/1.0");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
            if ((int)response.StatusCode == 403)
            {
                throw new HttpRequestException("GitHub API 速率限制，请稍后重试");
            }
            throw new HttpRequestException($"API 返回错误: {(int)response.StatusCode} - {Truncate(errorText, 200)}");
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var messageNode))
            {
                var errorMessage = messageNode.GetString();
                throw new InvalidOperationException($"GitHub API 错误: {errorMessage}");
            }
            throw new InvalidOperationException("Invalid response format from GitHub API.");
        }

        int index = 0;
        foreach (var item in root.EnumerateArray())
        {
            var type = ReadString(item, "type");
            if (type != "file")
            {
                continue;
            }

            var name = ReadString(item, "name");
            var downloadUrl = ReadString(item, "download_url");

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            // 只处理图片文件
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".gif" && extension != ".webp")
            {
                continue;
            }

            // 解码文件名
            var decodedName = Uri.UnescapeDataString(name);
            decodedName = Path.GetFileNameWithoutExtension(decodedName);

            // 构造图片 URL
            string imageUrl;
            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                imageUrl = downloadUrl;
            }
            else
            {
                imageUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/main/{path}/{Uri.EscapeDataString(name)}";
            }

            // 应用镜像加速到图片 URL
            imageUrl = ZhiJiaoHubMirrorSources.ApplyMirror(imageUrl, mirrorSource);

            images.Add(new ZhiJiaoHubImageItem(decodedName, imageUrl, index));
            index++;
        }

        return images;
    }

    private bool TryGetZhiJiaoHubFromCache(string cacheKey, out ZhiJiaoHubSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            if (_zhiJiaoHubCacheBySource.TryGetValue(cacheKey, out var cacheEntry) &&
                cacheEntry.ExpireAt > DateTimeOffset.UtcNow)
            {
                snapshot = cacheEntry.Snapshot;
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    private void SetZhiJiaoHubCache(string cacheKey, ZhiJiaoHubSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            // 使用较长的缓存时间（1小时），因为图片列表不常变化
            _zhiJiaoHubCacheBySource[cacheKey] = new ZhiJiaoHubCacheEntry(
                snapshot,
                DateTimeOffset.UtcNow.Add(TimeSpan.FromHours(1)));
        }
    }

    private readonly ZhiJiaoHubCacheService _zhiJiaoHubCacheService = new();

    public async Task<ZhiJiaoHubSyncResult> SyncZhiJiaoHubImagesAsync(
        string source,
        string mirrorSource,
        IProgress<(int Current, int Total, string Status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = ZhiJiaoHubSources.Normalize(source);
        var normalizedMirror = ZhiJiaoHubMirrorSources.Normalize(mirrorSource);

        try
        {
            var query = new ZhiJiaoHubQuery(normalizedSource, ForceRefresh: true, MirrorSource: normalizedMirror);
            var result = await GetZhiJiaoHubImagesAsync(query, cancellationToken);

            if (!result.Success || result.Data == null)
            {
                return new ZhiJiaoHubSyncResult(
                    false,
                    null,
                    0,
                    0,
                    0,
                    result.ErrorMessage ?? "Failed to fetch image list");
            }

            return await _zhiJiaoHubCacheService.SyncImagesAsync(
                normalizedSource,
                result.Data.Images,
                normalizedMirror,
                progress,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ZhiJiaoHubSyncResult(false, null, 0, 0, 0, ex.Message);
        }
    }

    public ZhiJiaoHubLocalSnapshot? LoadZhiJiaoHubLocalSnapshot(string source)
    {
        var normalizedSource = ZhiJiaoHubSources.Normalize(source);
        return _zhiJiaoHubCacheService.LoadLocalSnapshot(normalizedSource);
    }

    public bool HasZhiJiaoHubLocalCache(string source)
    {
        var normalizedSource = ZhiJiaoHubSources.Normalize(source);
        return _zhiJiaoHubCacheService.HasLocalCache(normalizedSource);
    }

    public async Task<RecommendationQueryResult<ZhiJiaoHubHybridSnapshot>> GetZhiJiaoHubHybridImagesAsync(
        string source,
        string mirrorSource,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = ZhiJiaoHubSources.Normalize(source);
        var normalizedMirror = ZhiJiaoHubMirrorSources.Normalize(mirrorSource);

        var localPathMap = _zhiJiaoHubCacheService.LoadLocalPathMap(normalizedSource);

        try
        {
            var query = new ZhiJiaoHubQuery(normalizedSource, ForceRefresh: true, MirrorSource: normalizedMirror);
            var result = await GetZhiJiaoHubImagesAsync(query, cancellationToken);

            if (!result.Success || result.Data == null)
            {
                return RecommendationQueryResult<ZhiJiaoHubHybridSnapshot>.Fail(
                    result.ErrorCode ?? "upstream_error",
                    result.ErrorMessage ?? "Failed to fetch image list");
            }

            var hybridImages = result.Data.Images.Select((img, idx) =>
            {
                var hasLocal = localPathMap.TryGetValue(img.Url, out var localPath);
                return new ZhiJiaoHubHybridImageItem(
                    img.Name,
                    img.Url,
                    hasLocal ? localPath : null,
                    idx,
                    hasLocal);
            }).ToList();

            var snapshot = new ZhiJiaoHubHybridSnapshot(
                hybridImages,
                normalizedSource,
                hybridImages.Count(i => i.IsCached),
                hybridImages.Count);

            return RecommendationQueryResult<ZhiJiaoHubHybridSnapshot>.Ok(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RecommendationQueryResult<ZhiJiaoHubHybridSnapshot>.Fail("upstream_network_error", ex.Message);
        }
    }

    public async Task<string?> DownloadAndCacheImageAsync(
        string source,
        ZhiJiaoHubImageItem image,
        string mirrorSource,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = ZhiJiaoHubSources.Normalize(source);
        var normalizedMirror = ZhiJiaoHubMirrorSources.Normalize(mirrorSource);

        return await _zhiJiaoHubCacheService.DownloadAndSaveImageAsync(
            normalizedSource,
            image.Name,
            image.Url,
            normalizedMirror,
            cancellationToken);
    }

    public Task StartBackgroundDownloadAsync(
        string source,
        IReadOnlyList<ZhiJiaoHubHybridImageItem> images,
        string mirrorSource,
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = ZhiJiaoHubSources.Normalize(source);
        var normalizedMirror = ZhiJiaoHubMirrorSources.Normalize(mirrorSource);

        return Task.Run(async () =>
        {
            var uncachedImages = images.Where(i => !i.IsCached).ToList();
            var total = uncachedImages.Count;
            var downloaded = 0;

            foreach (var image in uncachedImages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var localPath = await _zhiJiaoHubCacheService.DownloadAndSaveImageAsync(
                        normalizedSource,
                        image.Name,
                        image.RemoteUrl,
                        normalizedMirror,
                        cancellationToken);

                    if (localPath != null)
                    {
                        downloaded++;
                    }

                    onProgress?.Invoke(downloaded, total, image.Name);
                }
                catch
                {
                }
            }
        }, cancellationToken);
    }
}
