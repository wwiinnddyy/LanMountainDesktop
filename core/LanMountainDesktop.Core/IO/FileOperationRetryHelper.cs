using System;
using System.IO;
using System.Threading;

namespace LanMountainDesktop.Shared.IO;

/// <summary>
/// 瞬时文件锁（资源管理器、杀毒、另一个进程）下的重试口径只认这一处：
/// 120ms / 250ms / 500ms 三档，只吞 <see cref="IOException"/> 与 <see cref="UnauthorizedAccessException"/>，
/// 耗尽后把最后一条异常原样抛出去。
/// </summary>
/// <remarks>
/// 放在 Core 是因为宿主与启动器是两个二进制、却踩同一块磁盘。跨二进制没有共享日志器，
/// 所以重试告警走可注入的 <see cref="FailureNotice"/>：谁想在日志里看到重试，谁在启动时接一下。
/// 不接也不会吞错误——异常照旧抛出，只是少了"重试过"这条上下文。
/// </remarks>
public static class FileOperationRetryHelper
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500)
    ];

    /// <summary>重试/清理告警的出口，参数是 (类别, 消息, 异常)——与各二进制日志器的签名一致。</summary>
    public static Action<string, string, Exception>? FailureNotice { get; set; }

    public static void CopyWithRetry(string sourceFilePath, string destinationFilePath, bool overwrite, string category)
    {
        Retry(
            () => File.Copy(sourceFilePath, destinationFilePath, overwrite),
            category,
            $"Copy '{sourceFilePath}' -> '{destinationFilePath}'");
    }

    public static void MoveWithOverwriteRetry(string sourceFilePath, string destinationFilePath, string category)
    {
        Retry(
            () => File.Move(sourceFilePath, destinationFilePath, overwrite: true),
            category,
            $"Move '{sourceFilePath}' -> '{destinationFilePath}'");
    }

    public static void DeleteFileWithRetry(string filePath, string category)
    {
        Retry(
            () =>
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            },
            category,
            $"Delete file '{filePath}'");
    }

    public static void DeleteDirectoryWithRetry(string directoryPath, bool recursive, string category)
    {
        Retry(
            () =>
            {
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, recursive);
                }
            },
            category,
            $"Delete directory '{directoryPath}'");
    }

    /// <summary>
    /// "尽力删掉，删不掉也不影响主流程"的删除只认这一处（不重试，与上面那组带重试的区别开）。
    /// 收口前 <c>TryDeleteFile</c> 有 7 份、<c>TryDeleteDirectory</c> 有 6 份，散在宿主、启动器与安装器
    /// 三个二进制里，而且已经各自漂了：
    /// 7 份文件删除里只有 2 份会先把只读属性清掉——另 5 份删只读文件会静默失败，
    /// 症状就是卸载/清理跑完但 AppData 里还留着文件；
    /// 13 份里只有 1 份把失败报进日志，其余全是空的 <c>catch</c>，所以这类残留从来查不到原因。
    /// 这里取两边并集的口径：先清属性再删，失败走 <see cref="FailureNotice"/>。
    /// </summary>
    /// <returns>真的删掉了返回 true；路径不存在或删不动返回 false（不抛）。</returns>
    public static bool TryDeleteFile(string? filePath, string category)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
            File.Delete(filePath);
            return true;
        }
        catch (Exception ex)
        {
            NotifyFailure(category, $"Failed to delete file '{filePath}'.", ex);
            return false;
        }
    }

    /// <summary>
    /// 尽力删除目录。<paramref name="recursive"/> 保留各调用点原有语义：
    /// <c>DataStorageService</c> 那处要的就是"只删空目录"，改成递归会连带删掉用户数据。
    /// 这里不清属性（13 份原稿没有一份清目录属性，清了就是把"删不动"变成"改了目录属性还是删不动"的另一种口径），
    /// 非空 + <c>recursive: false</c> 会失败并回报，这是原行为。
    /// </summary>
    public static bool TryDeleteDirectory(string? directoryPath, bool recursive, string category)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return false;
        }

        try
        {
            Directory.Delete(directoryPath, recursive);
            return true;
        }
        catch (Exception ex)
        {
            NotifyFailure(category, $"Failed to delete directory '{directoryPath}'.", ex);
            return false;
        }
    }

    /// <summary>把一条 IO 告警送给接上的日志器；没人接就当没发生。</summary>
    public static void NotifyFailure(string category, string message, Exception exception)
    {
        FailureNotice?.Invoke(category, message, exception);
    }

    private static void Retry(Action action, string category, string operationDescription)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (IsRetriable(ex))
            {
                lastException = ex;
                if (attempt >= RetryDelays.Length)
                {
                    break;
                }

                var delay = RetryDelays[attempt];
                NotifyFailure(
                    category,
                    $"{operationDescription} failed on attempt {attempt + 1}. Retrying after {delay.TotalMilliseconds:0} ms.",
                    ex);
                Thread.Sleep(delay);
            }
        }

        if (lastException is not null)
        {
            throw lastException;
        }
    }

    private static bool IsRetriable(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException;
    }
}
