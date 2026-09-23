namespace LanMountainDesktop.Shared.IO;

/// <summary>
/// "把一个不可信的串压成能当文件名/目录名用的一段"只认这一处。收口前四份：宿主 <c>PlondsPackageStore</c>
/// 与安装器 <c>InstallerPlondsClient</c> 两份逐字相同（跨两个二进制——包目录命名两边必须一致，
/// 而这类抄本的错法是"改了宿主忘了安装器"），<c>PlondsPreparedPackageInstaller</c> 是同政策但兜底词不同的一份，
/// <c>WhiteboardNotePersistenceService</c> 是**另一种判据**（另带 120 字符截断，故意不并进来，别顺手并）。
///
/// 判据三条：非法字符逐个换成 <c>_</c>（不是删掉——删会让 <c>a/b</c> 与 <c>ab</c> 撞成同一段，
/// 磁盘上就是两个不同来源盖住彼此）、两端 <c>Trim</c>、压完什么都不剩才给兜底词。
/// 顺序是**先换后剪**：制表与换行在 Windows 上也算非法字符，所以它们先变成 <c>_</c>、永远走不到兜底词——
/// 这个顺序本身就是判据，别倒过来写。
/// 兜底词由调用方给：包目录 <c>unknown</c>、版本号一段 <c>0.0.0</c>，那是各自的策略，
/// 这一家不替它们统一（要统一得先有人拍板）。
/// 非法字符集按 <c>Path.GetInvalidFileNameChars()</c> 现取，平台相关是应有行为；
/// 测试只用两个平台都算非法的 <c>/</c> 钉形状（用 <c>\</c> 在 Linux 上是合法字符）。
/// </summary>
public static class PathSegmentSanitizer
{
    public static string Sanitize(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
