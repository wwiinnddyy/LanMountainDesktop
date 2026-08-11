using System;
using System.IO;
using System.Threading;

namespace LanMountainDesktop.Services;

internal static class FileOperationRetryHelper
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500)
    ];

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
                AppLogger.Warn(
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
