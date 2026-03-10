using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Downloader;

namespace LanMountainDesktop.Services;

public sealed record DownloadProgressInfo(
    long DownloadedBytes,
    long? TotalBytes,
    double Progress,
    bool IsResuming,
    bool IsParallel);

public sealed record DownloadOptions(
    long? ExpectedSizeBytes = null,
    int MaxParallelSegments = 4,
    int ParallelThresholdBytes = 8 * 1024 * 1024,
    int BufferSize = 128 * 1024);

public sealed record DownloadResult(
    bool Success,
    string? FilePath,
    string? ErrorMessage,
    bool UsedResume,
    bool UsedParallelDownload);

public sealed class ResumableDownloadService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DestinationGates =
        new(StringComparer.OrdinalIgnoreCase);

    public ResumableDownloadService(System.Net.Http.HttpClient httpClient)
    {
    }

    public async Task<DownloadResult> DownloadAsync(
        string source,
        string destinationFilePath,
        DownloadOptions? options = null,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        var normalizedOptions = NormalizeOptions(options);
        var fullDestinationPath = Path.GetFullPath(destinationFilePath);
        var destinationGate = DestinationGates.GetOrAdd(
            fullDestinationPath,
            static _ => new SemaphoreSlim(1, 1));

        await destinationGate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(source))
            {
                return await CopyLocalFileAsync(
                    source,
                    fullDestinationPath,
                    normalizedOptions,
                    progress,
                    cancellationToken);
            }

            if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri) ||
                (sourceUri.Scheme != Uri.UriSchemeHttp && sourceUri.Scheme != Uri.UriSchemeHttps))
            {
                return new DownloadResult(false, null, $"Unsupported download source '{source}'.", false, false);
            }

            return await DownloadRemoteFileAsync(
                sourceUri,
                fullDestinationPath,
                normalizedOptions,
                progress,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "Downloader",
                $"Download failed. Source='{source}'; Destination='{fullDestinationPath}'.",
                ex);
            return new DownloadResult(false, null, ex.Message, false, false);
        }
        finally
        {
            destinationGate.Release();
        }
    }

    private async Task<DownloadResult> CopyLocalFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        DownloadOptions options,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var fullSourcePath = Path.GetFullPath(sourceFilePath);
        var fullDestinationPath = Path.GetFullPath(destinationFilePath);
        var totalBytes = new FileInfo(fullSourcePath).Length;

        var tempFilePath = BuildTempFilePath(fullDestinationPath);
        PrepareDestination(fullDestinationPath);

        if (CanReuseCompletedDestination(fullDestinationPath, totalBytes))
        {
            progress?.Report(new DownloadProgressInfo(totalBytes, totalBytes, 1d, false, false));
            CleanupLocalPartialArtifacts(tempFilePath);
            return new DownloadResult(true, fullDestinationPath, null, false, false);
        }

        long existingBytes = 0;
        if (File.Exists(tempFilePath))
        {
            existingBytes = new FileInfo(tempFilePath).Length;
            if (existingBytes > totalBytes)
            {
                CleanupLocalPartialArtifacts(tempFilePath);
                existingBytes = 0;
            }
        }

        if (!File.Exists(tempFilePath))
        {
            await using var tempCreateStream = new FileStream(
                tempFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                options.BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        if (existingBytes >= totalBytes)
        {
            CompleteLocalCopy(tempFilePath, fullDestinationPath);
            progress?.Report(new DownloadProgressInfo(totalBytes, totalBytes, 1d, existingBytes > 0, false));
            return new DownloadResult(true, fullDestinationPath, null, existingBytes > 0, false);
        }

        await using var sourceStream = new FileStream(
            fullSourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            options.BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destinationStream = new FileStream(
            tempFilePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read,
            options.BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (existingBytes > 0)
        {
            sourceStream.Seek(existingBytes, SeekOrigin.Begin);
            destinationStream.Seek(existingBytes, SeekOrigin.Begin);
        }

        await CopyStreamAsync(
            sourceStream,
            destinationStream,
            existingBytes,
            totalBytes,
            progress,
            cancellationToken);

        CompleteLocalCopy(tempFilePath, fullDestinationPath);
        return new DownloadResult(true, fullDestinationPath, null, existingBytes > 0, false);
    }

    private async Task<DownloadResult> DownloadRemoteFileAsync(
        Uri sourceUri,
        string destinationFilePath,
        DownloadOptions options,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        PrepareDestination(destinationFilePath);

        if (CanReuseCompletedDestination(destinationFilePath, options.ExpectedSizeBytes))
        {
            var existingLength = new FileInfo(destinationFilePath).Length;
            progress?.Report(new DownloadProgressInfo(existingLength, options.ExpectedSizeBytes, 1d, false, false));
            CleanupDownloaderArtifacts(destinationFilePath);
            return new DownloadResult(true, destinationFilePath, null, false, false);
        }

        var usedResume = HasDownloaderResumeArtifacts(destinationFilePath);
        var usedParallelDownload = ShouldUseParallelDownload(options);
        var configuration = CreateConfiguration(options, usedParallelDownload);
        using var downloader = new DownloadService(configuration);

        downloader.DownloadProgressChanged += (_, args) =>
        {
            progress?.Report(MapProgress(args, options.ExpectedSizeBytes, usedResume, usedParallelDownload));
        };

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                downloader.CancelAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Warn(
                    "Downloader",
                    $"Failed to cancel Downloader request for '{destinationFilePath}'.",
                    ex);
            }
        });

        AppLogger.Info(
            "Downloader",
            $"Starting remote download. Source='{sourceUri}'; Destination='{destinationFilePath}'; Parallel={usedParallelDownload}; ChunkCount={configuration.ChunkCount}; Resume={usedResume}.");

        await downloader.DownloadFileTaskAsync(sourceUri.AbsoluteUri, destinationFilePath);

        if (!File.Exists(destinationFilePath))
        {
            throw new FileNotFoundException(
                $"Downloader completed without producing '{destinationFilePath}'.",
                destinationFilePath);
        }

        var finalLength = new FileInfo(destinationFilePath).Length;
        progress?.Report(new DownloadProgressInfo(
            finalLength,
            options.ExpectedSizeBytes ?? finalLength,
            1d,
            usedResume,
            usedParallelDownload));

        AppLogger.Info(
            "Downloader",
            $"Remote download completed. Source='{sourceUri}'; Destination='{destinationFilePath}'; Size={finalLength}; Parallel={usedParallelDownload}; Resume={usedResume}.");

        return new DownloadResult(true, destinationFilePath, null, usedResume, usedParallelDownload);
    }

    private static async Task CopyStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        long initialDownloadedBytes,
        long totalBytes,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        var downloadedBytes = initialDownloadedBytes;
        progress?.Report(new DownloadProgressInfo(downloadedBytes, totalBytes, downloadedBytes / (double)totalBytes, initialDownloadedBytes > 0, false));

        while (true)
        {
            var read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;
            progress?.Report(new DownloadProgressInfo(
                downloadedBytes,
                totalBytes,
                Math.Clamp(downloadedBytes / (double)totalBytes, 0d, 1d),
                initialDownloadedBytes > 0,
                false));
        }

        await destinationStream.FlushAsync(cancellationToken);
    }

    private static DownloadConfiguration CreateConfiguration(DownloadOptions options, bool useParallelDownload)
    {
        return new DownloadConfiguration
        {
            BufferBlockSize = options.BufferSize,
            ChunkCount = useParallelDownload ? options.MaxParallelSegments : 1,
            ParallelCount = useParallelDownload ? options.MaxParallelSegments : 1,
            ParallelDownload = useParallelDownload,
            MinimumSizeOfChunking = options.ParallelThresholdBytes,
            MaxTryAgainOnFailure = 3,
            ResumeDownloadIfCan = true,
            ClearPackageOnCompletionWithFailure = false,
            FileExistPolicy = FileExistPolicy.Delete,
            DownloadFileExtension = ".part"
        };
    }

    private static DownloadProgressInfo MapProgress(
        DownloadProgressChangedEventArgs args,
        long? expectedSizeBytes,
        bool isResuming,
        bool isParallel)
    {
        var totalBytes = args.TotalBytesToReceive > 0
            ? args.TotalBytesToReceive
            : expectedSizeBytes;
        var downloadedBytes = Math.Max(0L, args.ReceivedBytesSize);
        var normalizedProgress = args.ProgressPercentage > 1d
            ? args.ProgressPercentage / 100d
            : args.ProgressPercentage;

        if (totalBytes is > 0 && normalizedProgress <= 0d)
        {
            normalizedProgress = downloadedBytes / (double)totalBytes.Value;
        }

        return new DownloadProgressInfo(
            downloadedBytes,
            totalBytes,
            Math.Clamp(normalizedProgress, 0d, 1d),
            isResuming,
            isParallel);
    }

    private static bool CanReuseCompletedDestination(string destinationFilePath, long? expectedSizeBytes)
    {
        if (!File.Exists(destinationFilePath))
        {
            return false;
        }

        if (expectedSizeBytes is not > 0)
        {
            return false;
        }

        return new FileInfo(destinationFilePath).Length == expectedSizeBytes.Value;
    }

    private static void PrepareDestination(string destinationFilePath)
    {
        var directory = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void CleanupLocalPartialArtifacts(string tempFilePath)
    {
        if (File.Exists(tempFilePath))
        {
            FileOperationRetryHelper.DeleteFileWithRetry(tempFilePath, "Downloader");
        }
    }

    private static void CompleteLocalCopy(string tempFilePath, string destinationFilePath)
    {
        if (!File.Exists(tempFilePath))
        {
            return;
        }

        FileOperationRetryHelper.MoveWithOverwriteRetry(tempFilePath, destinationFilePath, "Downloader");
    }

    private static void CleanupDownloaderArtifacts(string destinationFilePath)
    {
        var transientFilePath = BuildTempFilePath(destinationFilePath);
        var metadataFilePath = BuildPackageFilePath(destinationFilePath);

        if (File.Exists(transientFilePath))
        {
            FileOperationRetryHelper.DeleteFileWithRetry(transientFilePath, "Downloader");
        }

        if (File.Exists(metadataFilePath))
        {
            FileOperationRetryHelper.DeleteFileWithRetry(metadataFilePath, "Downloader");
        }
    }

    private static bool HasDownloaderResumeArtifacts(string destinationFilePath)
    {
        return File.Exists(BuildTempFilePath(destinationFilePath)) ||
               File.Exists(BuildPackageFilePath(destinationFilePath));
    }

    private static bool ShouldUseParallelDownload(DownloadOptions options)
    {
        return options.MaxParallelSegments > 1;
    }

    private static DownloadOptions NormalizeOptions(DownloadOptions? options)
    {
        var normalized = options ?? new DownloadOptions();
        return normalized with
        {
            MaxParallelSegments = Math.Clamp(normalized.MaxParallelSegments, 1, 8),
            ParallelThresholdBytes = Math.Max(1_048_576, normalized.ParallelThresholdBytes),
            BufferSize = Math.Max(16 * 1024, normalized.BufferSize)
        };
    }

    private static string BuildTempFilePath(string destinationFilePath) => destinationFilePath + ".part";

    private static string BuildPackageFilePath(string destinationFilePath) => destinationFilePath + ".download";
}
