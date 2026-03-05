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
    private static readonly Regex HtmlTagRegex = new("<.*?>", RegexOptions.Compiled | RegexOptions.Singleline);

    private sealed record DailyArtworkCacheEntry(DailyArtworkSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record DailyPoetryCacheEntry(DailyPoetrySnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record DailyNewsCacheEntry(DailyNewsSnapshot Snapshot, DateTimeOffset ExpireAt);
    private sealed record DailyWordCacheEntry(DailyWordSnapshot Snapshot, DateTimeOffset ExpireAt);
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
    private DailyNewsCacheEntry? _dailyNewsCache;
    private DailyWordCacheEntry? _dailyWordCache;

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
            _dailyNewsCache = null;
            _dailyWordCache = null;
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
                Items: items.Take(targetCount).ToArray(),
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

        return string.Join("；", lines
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
