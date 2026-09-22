using System.Text.RegularExpressions;

namespace LanMountainDesktop.Helpers;

/// <summary>
/// 组件上那些"一行放不下就要压成一行"的短文本，归一化口径只认这一处。
/// 收口前 <c>NormalizeCompactText</c> 连同它专用的 <c>MultiWhitespaceRegex</c> 在 9 个组件里逐字抄了 9 遍，
/// 调用点 23 处。抄丢一处的症状很具体：同一个信息源，2×2 卡片上的标题把换行显示成一个洞，
/// 而 4×2 那张显示成两个空格——两张卡贴在同一页面上。
/// </summary>
internal static class CompactText
{
    private static readonly Regex MultiWhitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>去掉首尾空白，并把内部任意连续空白（含换行、制表、全角空格）压成单个半角空格；空输入给空串。</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return MultiWhitespace.Replace(text.Trim(), " ");
    }

    /// <summary>
    /// 裁到 <paramref name="maxLength"/> 个字符，裁过就补省略号，让人看得出"这里断过"；空输入给空串。
    /// 用于 HTTP 错误体这类要拼进异常/日志的长文本。收口前它抄了 4 份，其中 GitHub 更新服务那份
    /// 不带省略号——同一份报错，那边看着像完整回复。
    /// </summary>
    public static string Truncate(string? text, int maxLength)
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
