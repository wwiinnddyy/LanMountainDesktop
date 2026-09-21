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
