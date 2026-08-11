using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed record DailyArtworkQuery(
    string? Locale = null,
    string? MirrorSource = null,
    bool ForceRefresh = false);

public sealed record DailyPoetryQuery(
    string? Locale = null,
    bool ForceRefresh = false);

public sealed record DailyNewsQuery(
    string? Locale = null,
    int? ItemCount = null,
    bool ForceRefresh = false);

public sealed record IfengNewsQuery(
    string? Locale = null,
    int? ItemCount = null,
    string? ChannelType = null,
    bool ForceRefresh = false);

public sealed record BilibiliHotSearchQuery(
    string? Locale = null,
    int? ItemCount = null,
    bool ForceRefresh = false);

public sealed record BaiduHotSearchQuery(
    string? Locale = null,
    int? ItemCount = null,
    string? SourceType = null,
    bool ForceRefresh = false);

public sealed record DailyWordQuery(
    string? Locale = null,
    bool ForceRefresh = false);

public sealed record Stcn24ForumPostsQuery(
    string? Locale = null,
    int? ItemCount = null,
    string? SourceType = null,
    bool ForceRefresh = false);

public sealed record ExchangeRateQuery(
    string? BaseCurrency = null,
    string? TargetCurrency = null,
    bool ForceRefresh = false);

public sealed record ZhiJiaoHubQuery(
    string? Source = null,
    int? ImageIndex = null,
    bool ForceRefresh = false,
    string? MirrorSource = null);

public sealed record ZhiJiaoHubImageItem(
    string Name,
    string Url,
    int Index);

public sealed record ZhiJiaoHubSnapshot(
    IReadOnlyList<ZhiJiaoHubImageItem> Images,
    int CurrentIndex,
    string Source);

public sealed record ZhiJiaoHubHybridImageItem(
    string Name,
    string RemoteUrl,
    string? LocalPath,
    int Index,
    bool IsCached);

public sealed record ZhiJiaoHubHybridSnapshot(
    IReadOnlyList<ZhiJiaoHubHybridImageItem> Images,
    string Source,
    int CachedCount,
    int TotalCount);

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
        "https://api.artic.edu/api/v1/artworks?page={0}&limit={1}&fields=id,title,artist_title,artist_display,date_display,image_id,api_link,thumbnail";

    public string ArtInstituteImageUrlTemplate { get; init; } =
        "https://www.artic.edu/iiif/2/{0}/full/843,/0/default.jpg";

    public string DomesticArtworkApiUrl { get; init; } =
        "https://cn.bing.com/HPImageArchive.aspx?format=js&idx=0&n=8&mkt=zh-CN";

    public string DomesticArtworkHost { get; init; } = "https://cn.bing.com";

    public string CnrDailyNewsListUrl { get; init; } = "https://www.cnr.cn/newscenter/native/gd/";

    public IReadOnlyList<string> CnrDailyNewsRssFeedUrls { get; init; } =
    [
        "https://www.cnr.cn/rss.xml",
        "https://news.cnr.cn/rss.xml",
        "https://www.cnr.cn/newscenter/native/gd/rss.xml",
        "https://news.cnr.cn/native/gd/rss.xml"
    ];

    public IReadOnlyList<string> IfengNewsComprehensiveRssFeedUrls { get; init; } =
    [
        "https://rss.injahow.cn/ifeng/news",
        "https://rsshub.shuaizheng.org/ifeng/news"
    ];

    public IReadOnlyList<string> IfengNewsMainlandRssFeedUrls { get; init; } =
    [
        "https://rss.injahow.cn/ifeng/news/shanklist/3-35197-/",
        "https://rsshub.shuaizheng.org/ifeng/news/shanklist/3-35197-/"
    ];

    public IReadOnlyList<string> IfengNewsTaiwanRssFeedUrls { get; init; } =
    [
        "https://rss.injahow.cn/ifeng/news/shanklist/3-35199-/",
        "https://rsshub.shuaizheng.org/ifeng/news/shanklist/3-35199-/"
    ];

    public string IfengNewsComprehensiveListPageUrl { get; init; } = "https://news.ifeng.com/";

    public string IfengNewsMainlandListPageUrl { get; init; } = "https://news.ifeng.com/shanklist/3-35197-/";

    public string IfengNewsTaiwanListPageUrl { get; init; } = "https://news.ifeng.com/shanklist/3-35199-/";

    public string BilibiliHotSearchApiTemplate { get; init; } =
        "https://api.bilibili.com/x/web-interface/search/square?limit={0}";

    public string BilibiliSearchDefaultApiUrl { get; init; } =
        "https://api.bilibili.com/x/web-interface/search/default";

    public string BilibiliSearchPageUrl { get; init; } = "https://search.bilibili.com/all";

    public string BaiduHotSearchRssFeedUrl { get; init; } = "https://rss.aishort.top/?type=baidu";

    public string BaiduHotSearchBoardUrl { get; init; } = "https://top.baidu.com/board?tab=realtime";

    public string SmartTeachForumApiTemplate { get; init; } =
        "https://forum.smart-teach.cn/api/discussions?filter[q]={0}&sort=-createdAt&page[limit]={1}&include=user";

    public string SmartTeachForumBaseUrl { get; init; } = "https://forum.smart-teach.cn";

    public string SmartTeachStcnKeyword { get; init; } = "STCN";

    public string YoudaoDictionaryApiTemplate { get; init; } = "https://dict.youdao.com/jsonapi?q={0}";

    public string YoudaoDictionaryWordPageTemplate { get; init; } = "https://dict.youdao.com/w/eng/{0}/";

    public string ExchangeRateApiTemplate { get; init; } = "https://open.er-api.com/v6/latest/{0}";

    public IReadOnlyList<string> YoudaoDailyWordCandidates { get; init; } =
    [
        "illustrate",
        "resilient",
        "meticulous",
        "coherent",
        "subtle",
        "constrain",
        "tangible",
        "versatile",
        "pragmatic",
        "derive",
        "intricate",
        "notion",
        "facilitate",
        "sustain",
        "clarify",
        "convey",
        "nuance",
        "transform",
        "navigate",
        "align",
        "elevate",
        "refine",
        "vivid",
        "compile",
        "inspect",
        "aggregate",
        "optimize",
        "resonate",
        "persist",
        "adapt",
        "emerge",
        "concrete",
        "articulate",
        "validate",
        "insight",
        "concise",
        "robust",
        "reliable",
        "spectrum",
        "landscape",
        "context",
        "constraint",
        "iterative",
        "foundation",
        "priority",
        "workflow",
        "synthesize",
        "anchor",
        "precision",
        "momentum",
        "integrate",
        "observe",
        "structure",
        "essence",
        "framework",
        "drift",
        "discern",
        "compose",
        "modulate",
        "stability",
        "trajectory",
        "analyze",
        "diagnose",
        "mitigate",
        "transparent",
        "progressive",
        "boundary",
        "allocate",
        "evaluate",
        "reconcile",
        "strategic",
        "holistic",
        "incremental",
        "temporal",
        "semantic",
        "parallel",
        "explicit",
        "objective",
        "capacity",
        "durable",
        "scalable",
        "residual",
        "verify",
        "discover",
        "curate",
        "invoke",
        "artistry",
        "sincere",
        "substantive",
        "deliberate",
        "dynamic",
        "intentional",
        "initiative",
        "evidence",
        "infuse",
        "harmony",
        "vitality",
        "polish",
        "portrait",
        "rhythm",
        "accent",
        "gradient",
        "palette",
        "pattern",
        "eclipse",
        "horizon",
        "luminous",
        "serene",
        "vantage",
        "kinetic",
        "refactor",
        "calibrate",
        "orchestrate",
        "prototype",
        "curiosity",
        "discipline",
        "inscribe",
        "engage",
        "spark",
        "zenith",
        "clarity",
        "resolve",
        "aptitude"
    ];

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public int DefaultArtworkCandidateCount { get; init; } = 50;

    public int DefaultDailyNewsCount { get; init; } = 2;

    public int DefaultIfengNewsCount { get; init; } = 4;

    public int DefaultBilibiliHotSearchCount { get; init; } = 5;

    public int DefaultBaiduHotSearchCount { get; init; } = 4;

    public int DefaultStcn24ForumPostCount { get; init; } = 4;

    public string ClassIslandHubApiUrl { get; init; } = "https://api.github.com/repos/ClassIsland/classisland-hub/contents/images";

    public string SectlHubApiUrl { get; init; } = "https://api.github.com/repos/SECTL/SECTL-hub/contents/docs/.vuepress/public/images";

    public string RinLitHubApiUrl { get; init; } = "https://api.github.com/repos/RinLit-233-shiroko/Rin-sHub/contents/images";

    public string ClassIslandHubRawUrlTemplate { get; init; } = "https://raw.githubusercontent.com/ClassIsland/classisland-hub/main/images/{0}";

    public string SectlHubRawUrlTemplate { get; init; } = "https://raw.githubusercontent.com/SECTL/SECTL-hub/main/docs/.vuepress/public/images/{0}";

    public string RinLitHubRawUrlTemplate { get; init; } = "https://raw.githubusercontent.com/RinLit-233-shiroko/Rin-sHub/main/images/{0}";
}

public interface IRecommendationInfoService
{
    Task<RecommendationQueryResult<DailyArtworkSnapshot>> GetDailyArtworkAsync(
        DailyArtworkQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<DailyPoetrySnapshot>> GetDailyPoetryAsync(
        DailyPoetryQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<DailyNewsSnapshot>> GetDailyNewsAsync(
        DailyNewsQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<DailyNewsSnapshot>> GetIfengNewsAsync(
        IfengNewsQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<BilibiliHotSearchSnapshot>> GetBilibiliHotSearchAsync(
        BilibiliHotSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<BaiduHotSearchSnapshot>> GetBaiduHotSearchAsync(
        BaiduHotSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<DailyWordSnapshot>> GetDailyWordAsync(
        DailyWordQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<Stcn24ForumPostsSnapshot>> GetStcn24ForumPostsAsync(
        Stcn24ForumPostsQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<ExchangeRateSnapshot>> GetExchangeRateAsync(
        ExchangeRateQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<ZhiJiaoHubSnapshot>> GetZhiJiaoHubImagesAsync(
        ZhiJiaoHubQuery query,
        CancellationToken cancellationToken = default);

    Task<RecommendationQueryResult<ZhiJiaoHubHybridSnapshot>> GetZhiJiaoHubHybridImagesAsync(
        string source,
        string mirrorSource,
        CancellationToken cancellationToken = default);

    Task<string?> DownloadAndCacheImageAsync(
        string source,
        ZhiJiaoHubImageItem image,
        string mirrorSource,
        CancellationToken cancellationToken = default);

    Task StartBackgroundDownloadAsync(
        string source,
        IReadOnlyList<ZhiJiaoHubHybridImageItem> images,
        string mirrorSource,
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default);

    Task<ZhiJiaoHubSyncResult> SyncZhiJiaoHubImagesAsync(
        string source,
        string mirrorSource,
        IProgress<(int Current, int Total, string Status)>? progress = null,
        CancellationToken cancellationToken = default);

    ZhiJiaoHubLocalSnapshot? LoadZhiJiaoHubLocalSnapshot(string source);

    bool HasZhiJiaoHubLocalCache(string source);

    void ClearCache();
}
