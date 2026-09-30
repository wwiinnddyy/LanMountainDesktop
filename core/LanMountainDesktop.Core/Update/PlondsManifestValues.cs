namespace LanMountainDesktop.Shared.Contracts.Update;

/// <summary>
/// 从 PLONDS 清单里读到的两个值"该怎么归一"只认这一处。
///
/// 为什么值得收：收口前宿主（<c>PlondsVerifier</c> / <c>PlondsClientServiceFactory</c>）与安装器
/// （<c>InstallerPlondsClient</c>）各写了一份**逐字相同**的实现，而两个二进制都在引用 Core——
/// 于是"同一份清单、同一个环境变量，宿主与安装器可能认成不同东西"这件事只靠巧合避免。
/// 改一份的症状不是崩：哈希那处是校验"过与不过"，URL 那处是"从哪个源取"，两边漂开就是
/// 安装器说没问题、宿主说签名不符（或反过来），而两份日志都自洽。
///
/// 有一条**故意没并进来**：<c>desktop/.../Services/Update/UpdateHash.cs</c> 的
/// <c>NormalizeHashText</c> 判的不是这条规则——它剥 <c>algo:</c> 前缀、去掉 <c>-</c>，而这里这条
/// 去的是空格、保留 <c>-</c>。看着像重复，实际是两条不同的宽容度；把它们"顺手统一"会改变校验的
/// 通过集合（属于安全判定），所以两份各留各家，只把**跨二进制逐字相同**的那两份收进来。
/// </summary>
public static class PlondsManifestValues
{
    /// <summary>
    /// 哈希文本归一：去首尾空白、去掉中间所有空格、转小写。
    /// 保留 <c>-</c> 是有意的——清单里的哈希是十六进制，带 <c>-</c> 的写法走 <c>UpdateHash.NormalizeHashText</c>。
    /// </summary>
    public static string NormalizeHash(string value)
    {
        return value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    /// <summary>环境变量覆盖了就用覆盖值（去掉首尾空白），没设或全是空白才用内置默认。</summary>
    public static string ResolveManifestUrl(string environmentVariable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
