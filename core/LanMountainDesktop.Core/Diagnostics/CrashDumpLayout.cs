using System;
using System.IO;

namespace LanMountainDesktop.Shared.Diagnostics;

/// <summary>
/// 崩溃转储的磁盘契约：宿主写、启动器读，两个二进制之间没有共享类型，全靠这几个名字逐字对齐。
/// 收口前 <c>crashes</c> / <c>crash-*.txt</c> / <c>latest.txt</c> 分散在 4 处各自抄写
/// （<c>Program.WriteCrashDump</c> 写，<c>LauncherGuiCoordinator</c> 与 <c>ErrorWindow</c> 读），
/// 改错一个字母的症状是崩溃对话框安静地什么都不显示——恰好是最需要诊断信息的那一刻。
/// </summary>
public static class CrashDumpLayout
{
    /// <summary>转储目录名，位于 LocalApplicationData/LanMountainDesktop 之下。</summary>
    public const string DirectoryName = "crashes";

    /// <summary>读一方枚举转储用的通配；写一方必须用 <see cref="BuildDumpFileName"/>。</summary>
    public const string DumpFilePattern = "crash-*.txt";

    /// <summary>指向最近一次崩溃转储的标记文件，内容是那条绝对路径。</summary>
    public const string LatestMarkerFileName = "latest.txt";

    private const string AppDirectoryName = "LanMountainDesktop";

    private const string DumpFileNameFormat = "yyyyMMdd_HHmmss";

    /// <summary><c>{LocalApplicationData}/LanMountainDesktop/crashes</c>。</summary>
    public static string ResolveDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDirectoryName,
        DirectoryName);

    /// <summary>
    /// 由崩溃时刻生成转储文件名。写出的名字必须落在 <see cref="DumpFilePattern"/> 里，
    /// 所以这个算式和通配得住在同一处。
    /// </summary>
    public static string BuildDumpFileName(DateTime localTime) =>
        $"crash-{localTime.ToString(DumpFileNameFormat, System.Globalization.CultureInfo.InvariantCulture)}.txt";
}
