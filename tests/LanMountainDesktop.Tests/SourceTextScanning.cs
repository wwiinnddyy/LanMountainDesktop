using System.Text.RegularExpressions;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 按行扫描源码文本时用的小工具：把字符串字面量的**引号内文本**拿掉，但保留插值洞 <c>{expr}</c> 里的表达式。
///
/// 为什么不能整段拿掉：宿主里真实的调用点会写在插值洞里——实测样本
/// <c>RssReaderWidget.axaml.cs:113</c> 的 <c>$"{entry.SourceTitle} · {FormatRelative(entry.PublishedAt)}…"</c>。
/// 第一版把整串换掉，棘轮当场把 <c>FormatRelative</c> 报成零引用（假阳性）。
/// 反过来也不能不剥：日志分类名与类型/成员同名会把计数喂饱，实测整类没被 new 的
/// <c>LoadingTimeoutHandler</c> 就是这么躲过 <see cref="ZeroUseTypeRatchetTests"/> 的。
///
/// 只做行内处理（与两条棘轮一样按行读），所以跨行的 verbatim/raw 字符串剥不干净：
/// 那种情况只会"少剥"，方向是漏报不是误报。
/// </summary>
internal static class SourceTextScanning
{
    private static readonly Regex Literal = new(
        @"""(?:[^""\\]|\\.)*""",
        RegexOptions.Compiled);

    /// <summary>把这一行里所有字符串字面量的引号内文本换成空格，插值洞原样留下。</summary>
    public static string WithoutStringLiteralText(string line)
    {
        return Literal.Replace(line, match => KeepInterpolationHoles(match.Value));
    }

    private static string KeepInterpolationHoles(string literal)
    {
        var builder = new System.Text.StringBuilder(literal.Length);
        var body = literal.TrimStart('@', '$');
        if (body.StartsWith("\"\"", StringComparison.Ordinal))
        {
            // raw 字符串（"""…"""）：整段按字面处理，插值洞仍按 { } 取，跨行不解析。
            body = body.TrimStart('"');
        }

        var index = 0;
        while (index < body.Length)
        {
            var open = body.IndexOf('{', index);
            if (open < 0)
            {
                break;
            }

            if (open + 1 < body.Length && body[open + 1] == '{')
            {
                index = open + 2;   // {{ 是转义的花括号，不是洞
                continue;
            }

            var close = body.IndexOf('}', open + 1);
            if (close < 0)
            {
                break;
            }

            builder.Append(' ').Append(body, open + 1, close - open - 1);
            index = close + 1;
        }

        return builder.ToString();
    }
}
