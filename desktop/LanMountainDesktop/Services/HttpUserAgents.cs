namespace LanMountainDesktop.Services;

/// <summary>
/// 宿主对外发请求时报出去的身份字符串，全部登记在这里。分两种用途：伪装浏览器
/// （第三方新闻/图片 CDN 会掐掉没有浏览器指纹的请求）与自报家门（自家市场、更新、RSS 用，对端据此放行与统计）。
/// 收口前"完整浏览器指纹"这一份在 4 个组件里逐字抄了 4 遍，市场身份在 4 个市场服务里抄了 4 遍，
/// 裸产品名在 3 处抄了 3 遍。抄漏一处的症状不是崩，是"某个组件的图片突然 403"，
/// 而且只在那一家 CDN 改口径的那天出现。
///
/// 这里收的是"**发出去的字节逐字相同**"的那些。历史上还有第三种口径：
/// <see cref="BrowserMinimal"/> 只写到 <c>Mozilla/5.0</c>，是 <c>RecommendationDataService</c> 给 9 个
/// 第三方接口用的。它和 <see cref="Browser"/> 是两种对外身份，不是同一份真源的副本——
/// 把它们统一会改变真正发出去的字节，属于要拍板的事，不是收口能顺手做的。
///
/// 还有第四种不在此列：<c>Plonds/PlondsHttpClientFactory</c> 用
/// <c>ProductInfoHeaderValue</c> 现拼 <c>LanMountainDesktop/&lt;程序集版本&gt;</c>，是唯一一份版本号会随发布走的。
/// 其余几份里的 <c>/1.0</c> 全是写死的，产品版本涨了就没人动它——这件事也登记等拍板。
/// </summary>
internal static class HttpUserAgents
{
    /// <summary>第三方内容 CDN 认的浏览器指纹，必须逐字是一个真实 Chrome UA。</summary>
    public const string Browser =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";

    /// <summary>只声明 Gecko 内核的短指纹：<c>RecommendationDataService</c> 的 9 个接口在用，别再往别处扩散。</summary>
    public const string BrowserMinimal = "Mozilla/5.0";

    /// <summary>AirApp 市场四个服务（索引、图标、README、安装）共用的身份。</summary>
    public const string AirAppMarketplace = "LanMountainDesktop-AirAppMarketplace/1.0";

    /// <summary>拉取跨 AirApp 共享契约程序集时的身份。</summary>
    public const string SharedContracts = "LanMountainDesktop-SharedContracts/1.0";

    /// <summary>PLONDS 更新检查（GitHub Releases）用的身份。</summary>
    public const string Updater = "LanMountainDesktop-Updater/1.0";

    /// <summary>RSS 抓取用的身份。</summary>
    public const string RssReader = "LanMountainDesktop-RssReader/1.0";

    /// <summary>没有子系统后缀时用的裸产品身份。</summary>
    public const string Product = "LanMountainDesktop/1.0";
}
