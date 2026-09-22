using System;
using System.IO;

namespace LanMountainDesktop.Shared.IO;

/// <summary>
/// "这条路径确实存在，并且给我一个能拿来比较的全路径"——唯一一份实现。
/// 此前 Core 的 <c>AppVersionProvider</c>（启动器读部署目录/可执行文件）与宿主的
/// <c>AppRestartService</c>（重启时找回自己）各抄了一份逐字相同的 17 行，共 4 个方法。
/// 两份算的不是磁盘布局而是"这条路径能不能信"，漂开的后果不是崩，
/// 而是同一个安装被两个进程判成不一样的结论：启动器认为有效的部署目录，宿主重启时当作不存在。
///
/// 空、纯空白、以及 <c>Path.GetFullPath</c> 会抛的非法路径一律回 <c>null</c>——
/// 调用方全是"有就用、没有就走兜底"的形状，这里抛出去只会把重启变成崩溃。
/// <para>
/// 一件<b>没有</b>替调用方做的事：尾部目录分隔符原样保留（<c>C:\dir\</c> 与 <c>C:\dir</c> 算出来是两个串）。
/// 这是被收口的那两份实现本来就有的行为，收口不改语义，所以由 <c>ExistingPathTests</c> 钉成"现状"；
/// 要改就在这一个地方改，但它会同时改变 10 个调用点拿返回值做相等比较的结果——另立 G1-BI 等拍板。
/// </para>
/// </summary>
public static class ExistingPath
{
    public static string? DirectoryOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    public static string? FileOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }
}
