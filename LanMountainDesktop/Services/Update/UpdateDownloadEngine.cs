using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

public sealed record DownloadResult(bool Success, string? FilePath, string? ErrorMessage, bool HashVerified);

internal sealed class UpdateDownloadEngine
{
    private readonly IUpdateManifestProvider _manifestProvider;
    private readonly ResumableDownloadService _downloadService;

    private const int MaxRetryAttempts = 3;
    private const int RetryDelayMs = 1000;

    public UpdateDownloadEngine(
        IUpdateManifestProvider manifestProvider,
        ResumableDownloadService downloadService)
    {
        _manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
        _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
    }

    public async Task<DownloadResult> DownloadPayloadAsync(
        UpdateManifest manifest,
        string incomingDirectory,
        string objectsDirectory,
        int maxConcurrency,
        IProgress<DownloadProgressReport>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        try
        {
            Directory.CreateDirectory(incomingDirectory);
            Directory.CreateDirectory(objectsDirectory);
        }
        catch (Exception ex)
        {
            return new DownloadResult(false, null, $"Failed to create download directories: {ex.Message}", false);
        }

        var fileMapPath = Path.Combine(incomingDirectory, UpdatePaths.GetPlondsFileMapName());
        var signaturePath = Path.Combine(incomingDirectory, UpdatePaths.GetPlondsSignatureName());

        try
        {
            if (manifest.FileMapUrl is not null)
            {
                await DownloadWithRetryAsync(manifest.FileMapUrl, fileMapPath, ct);
            }

            if (manifest.FileMapSignatureUrl is not null)
            {
                await DownloadWithRetryAsync(manifest.FileMapSignatureUrl, signaturePath, ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DownloadResult(false, null, $"Failed to download file map: {ex.Message}", false);
        }

        var downloadableFiles = manifest.Files
            .Where(f => f.Action is not ("reuse" or "delete") && !string.IsNullOrWhiteSpace(f.ObjectUrl))
            .ToList();

        var totalFiles = downloadableFiles.Count + 2;
        var completedFiles = 2;
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrency), Math.Max(1, maxConcurrency));
        var errors = new List<string>();
        long totalBytes = downloadableFiles.Sum(f => f.Size);
        long downloadedBytes = 0;
        var lockObj = new object();

        var tasks = downloadableFiles.Select(async entry =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (!seenHashes.Add(entry.Sha256))
                {
                    lock (lockObj)
                    {
                        completedFiles++;
                    }

                    ReportProgress(progress, entry.Path, downloadedBytes, totalBytes, completedFiles, totalFiles);
                    return;
                }

                var objectPath = GetObjectDestinationPath(objectsDirectory, entry.Sha256);
                var objectDir = Path.GetDirectoryName(objectPath);
                if (!string.IsNullOrWhiteSpace(objectDir))
                {
                    Directory.CreateDirectory(objectDir);
                }

                if (File.Exists(objectPath))
                {
                    var existingHash = await ComputeFileSha256Async(objectPath, ct);
                    if (string.Equals(existingHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        lock (lockObj)
                        {
                            completedFiles++;
                            downloadedBytes += entry.Size;
                        }

                        ReportProgress(progress, entry.Path, downloadedBytes, totalBytes, completedFiles, totalFiles);
                        return;
                    }
                }

                if (string.IsNullOrWhiteSpace(entry.ObjectUrl))
                {
                    return;
                }

                for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
                {
                    ct.ThrowIfCancellationRequested();

                    var result = await _downloadService.DownloadAsync(
                        entry.ObjectUrl,
                        objectPath,
                        cancellationToken: ct);

                    if (result.Success)
                    {
                        var actualHash = await ComputeFileSha256Async(objectPath, ct);
                        var hashVerified = string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase);

                        if (!hashVerified)
                        {
                            AppLogger.Warn("UpdateDownloadEngine",
                                $"Object {entry.Path} hash mismatch after download. Expected: {entry.Sha256}, Actual: {actualHash}");
                        }

                        lock (lockObj)
                        {
                            completedFiles++;
                            downloadedBytes += entry.Size;
                        }

                        ReportProgress(progress, entry.Path, downloadedBytes, totalBytes, completedFiles, totalFiles);
                        return;
                    }

                    if (attempt < MaxRetryAttempts)
                    {
                        AppLogger.Warn("UpdateDownloadEngine",
                            $"Object {entry.Path} download attempt {attempt}/{MaxRetryAttempts} failed: {result.ErrorMessage}. Retrying.");
                        await Task.Delay(RetryDelayMs * attempt, ct);
                    }
                    else
                    {
                        lock (lockObj)
                        {
                            errors.Add($"Failed to download {entry.Path}: {result.ErrorMessage}");
                        }
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException))
        {
            throw new OperationCanceledException(ct);
        }

        if (errors.Count > 0)
        {
            return new DownloadResult(false, null, string.Join("; ", errors), false);
        }

        var markerPath = Path.Combine(incomingDirectory, ".download-complete");
        try
        {
            var manifestSha256 = ComputeStringSha256(System.Text.Json.JsonSerializer.Serialize(manifest));
            var markerContent = UpdatePaths.GetDownloadMarkerContent(manifestSha256, manifest.ToVersion, downloadableFiles.Count);
            await File.WriteAllTextAsync(markerPath, markerContent, ct);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateDownloadEngine", $"Failed to write download marker: {ex.Message}");
        }

        AppLogger.Info("UpdateDownloadEngine", $"Delta payload downloaded to {incomingDirectory}. {downloadableFiles.Count} objects processed.");
        return new DownloadResult(true, incomingDirectory, null, true);
    }

    public async Task<DownloadResult> DownloadFullInstallerAsync(
        UpdateManifest manifest,
        string destinationPath,
        int maxThreads,
        IProgress<DownloadProgressReport>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.InstallerMirrors is null || manifest.InstallerMirrors.Count == 0)
        {
            return new DownloadResult(false, null, "No installer mirrors available.", false);
        }

        var mirror = manifest.InstallerMirrors.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Url));
        if (mirror is null || string.IsNullOrWhiteSpace(mirror.Url))
        {
            return new DownloadResult(false, null, "No usable installer mirror URL found.", false);
        }

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(destinationPath) && !string.IsNullOrWhiteSpace(mirror.Sha256))
        {
            var existingHash = await ComputeFileSha256Async(destinationPath, ct);
            if (string.Equals(existingHash, mirror.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info("UpdateDownloadEngine", "Full installer already downloaded with matching hash, skipping.");
                return new DownloadResult(true, destinationPath, null, true);
            }
        }

        var downloadProgress = progress is null ? null : new Progress<DownloadProgressInfo>(p =>
        {
            progress.Report(new DownloadProgressReport(
                Path.GetFileName(destinationPath),
                p.DownloadedBytes,
                p.TotalBytes ?? 0,
                0,
                0,
                1,
                p.Progress));
        });

        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await _downloadService.DownloadAsync(
                mirror.Url,
                destinationPath,
                new DownloadOptions(MaxParallelSegments: Math.Max(1, maxThreads)),
                downloadProgress,
                ct);

            if (result.Success)
            {
                bool hashVerified;
                if (!string.IsNullOrWhiteSpace(mirror.Sha256))
                {
                    var actualHash = await ComputeFileSha256Async(destinationPath, ct);
                    hashVerified = string.Equals(actualHash, mirror.Sha256, StringComparison.OrdinalIgnoreCase);
                    if (!hashVerified)
                    {
                        AppLogger.Warn("UpdateDownloadEngine",
                            $"Full installer hash mismatch. Expected: {mirror.Sha256}, Actual: {actualHash}");
                    }
                }
                else
                {
                    hashVerified = false;
                }

                AppLogger.Info("UpdateDownloadEngine", $"Full installer downloaded to {destinationPath}");
                return new DownloadResult(true, destinationPath, null, hashVerified);
            }

            if (attempt < MaxRetryAttempts)
            {
                AppLogger.Warn("UpdateDownloadEngine",
                    $"Full installer download attempt {attempt}/{MaxRetryAttempts} failed: {result.ErrorMessage}. Retrying.");
                await Task.Delay(RetryDelayMs * attempt, ct);
            }
            else
            {
                return new DownloadResult(false, null, $"Failed to download full installer after {MaxRetryAttempts} attempts: {result.ErrorMessage}", false);
            }
        }

        return new DownloadResult(false, null, "Failed to download full installer.", false);
    }

    private static string GetObjectDestinationPath(string objectsDirectory, string objectHashHex)
    {
        var normalized = objectHashHex.Trim().ToLowerInvariant();
        var shard = normalized.Length >= 2 ? normalized[..2] : normalized;
        return Path.Combine(objectsDirectory, shard, normalized);
    }

    private static void ReportProgress(
        IProgress<DownloadProgressReport>? progress,
        string currentFile,
        long bytesDownloaded,
        long bytesTotal,
        int filesCompleted,
        int filesTotal)
    {
        if (progress is null)
        {
            return;
        }

        var fraction = filesTotal > 0 ? (double)filesCompleted / filesTotal : 0;
        progress.Report(new DownloadProgressReport(
            currentFile,
            bytesDownloaded,
            bytesTotal,
            0,
            filesCompleted,
            filesTotal,
            fraction));
    }

    private async Task DownloadWithRetryAsync(string url, string destinationPath, CancellationToken ct)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await _downloadService.DownloadAsync(url, destinationPath, cancellationToken: ct);
            if (result.Success)
            {
                return;
            }

            lastError = new InvalidOperationException(result.ErrorMessage ?? "Download failed.");

            if (attempt < MaxRetryAttempts)
            {
                AppLogger.Warn("UpdateDownloadEngine",
                    $"Download of {url} attempt {attempt}/{MaxRetryAttempts} failed. Retrying.");
                await Task.Delay(RetryDelayMs * attempt, ct);
            }
        }

        throw lastError!;
    }

    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken ct)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        using var hasher = SHA256.Create();
        var hash = await hasher.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeStringSha256(string content)
    {
        using var hasher = SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = hasher.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
