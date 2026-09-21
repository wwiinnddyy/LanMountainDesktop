using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Shared.IO;

/// <summary>
/// "整体替换一个文件"的唯一入口：同目录写一个带 Guid 的临时文件，再用带重试的 Move 覆盖目标。
///
/// 收口前宿主里有 12 处各写一份这套动作，Launcher 与安装器另有各自的版本，其中两类是会真实咬人的差异：
/// 1) 目标文件被资源管理器/杀毒/另一进程瞬时锁住时，直接抛出去只留一条 Warn —— 用户看到的就是"设置没存上"。
///    <see cref="FileOperationRetryHelper"/> 本来就是为这种情况写的，却没被这套写盘用上。
/// 2) 临时文件名有的用固定 ".tmp"（两个写者会互相覆盖、Delete+Move 之间断电就把文件丢了），
///    而且 Move 失败后没人清理，用户的 AppData 里会攒下一堆 .tmp。
/// </summary>
/// <remarks>
/// 住在 Core 而不是宿主，是因为写同一批磁盘文件的是两个进程：宿主写 settings.json，
/// 首启向导（Launcher）也写它。放在任一侧都得被另一侧抄一遍。
/// </remarks>
public static class AtomicFileWriter
{
    /// <summary>不带显式编码：与 <see cref="File.WriteAllText(string,string)"/> 一致，UTF-8 无 BOM。</summary>
    public static void WriteText(string filePath, string content, string category)
    {
        Write(filePath, content, encoding: null, category);
    }

    /// <summary>
    /// 显式编码只给"磁盘上已经是这个字节形状"的旧文件用（如白板笔记带 BOM），
    /// 新代码请走无编码的重载，别把 BOM 扩散到还没落盘过的文件上。
    /// </summary>
    public static void WriteText(string filePath, string content, Encoding encoding, string category)
    {
        Write(filePath, content, encoding, category);
    }

    /// <summary>
    /// 流版：<c>File.Delete(target)</c> 后 <c>File.Move(temp, target)</c> 那种写法在两步之间会留下
    /// "目标文件不存在"的窗口，而且固定 <c>.tmp</c> 名会让两个写者互相踩。这里同样用唯一临时名 + 覆盖式 Move。
    /// </summary>
    public static async Task WriteStreamAsync(
        string filePath,
        Stream content,
        string category,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var target = File.Create(tempPath))
            {
                await content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            FileOperationRetryHelper.MoveWithOverwriteRetry(tempPath, filePath, category);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// 把磁盘上已有的一个文件原子地"放"到目标位置：同样走唯一临时名 + 带重试的覆盖式 Move。
    /// 与 <see cref="WriteStreamAsync"/> 的区别只是内容来源是现成文件（换壁纸、落下载包这类
    /// "用户选了一张图，系统要把它换成受管文件名"的场合）。源文件保留，删除由调用方决定。
    /// </summary>
    public static void PlaceFile(string sourceFilePath, string destinationFilePath, string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        var directory = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{destinationFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            FileOperationRetryHelper.CopyWithRetry(sourceFilePath, tempPath, overwrite: false, category);
            FileOperationRetryHelper.MoveWithOverwriteRetry(tempPath, destinationFilePath, category);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void Write(string filePath, string content, Encoding? encoding, string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 临时文件必须落在目标文件同一目录，Move 才是同一卷上的原子改名。
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            if (encoding is null)
            {
                File.WriteAllText(tempPath, content);
            }
            else
            {
                File.WriteAllText(tempPath, content, encoding);
            }

            FileOperationRetryHelper.MoveWithOverwriteRetry(tempPath, filePath, category);
        }
        finally
        {
            // Move 成功时临时文件已经不存在；只有失败路径需要收拾，别让 .tmp 留在用户目录里。
            TryDelete(tempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileOperationRetryHelper.NotifyFailure("AtomicFileWriter", $"Failed to remove temp file '{path}'.", ex);
        }
    }
}
