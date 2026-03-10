using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
    private static readonly JsonSerializerOptions MetadataSerializerOptions = new()
    {
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;

    public ResumableDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
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
        try
        {
            if (File.Exists(source))
            {
                return await CopyLocalFileAsync(
                    source,
                    destinationFilePath,
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
                destinationFilePath,
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
            return new DownloadResult(false, null, ex.Message, false, false);
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
        var metadataFilePath = BuildMetadataFilePath(fullDestinationPath);
        PrepareDestination(fullDestinationPath);

        if (CanReuseCompletedDestination(fullDestinationPath, totalBytes))
        {
            progress?.Report(new DownloadProgressInfo(totalBytes, totalBytes, 1d, false, false));
            CleanupPartialArtifacts(tempFilePath, metadataFilePath);
            return new DownloadResult(true, fullDestinationPath, null, false, false);
        }

        long existingBytes = 0;
        if (File.Exists(tempFilePath))
        {
            existingBytes = new FileInfo(tempFilePath).Length;
            if (existingBytes > totalBytes)
            {
                ResetPartialArtifacts(tempFilePath, metadataFilePath);
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
            CompleteDownload(tempFilePath, fullDestinationPath, metadataFilePath);
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
            isResuming: existingBytes > 0,
            isParallel: false,
            options.BufferSize,
            progress,
            cancellationToken);

        CompleteDownload(tempFilePath, fullDestinationPath, metadataFilePath);
        return new DownloadResult(true, fullDestinationPath, null, existingBytes > 0, false);
    }

    private async Task<DownloadResult> DownloadRemoteFileAsync(
        Uri sourceUri,
        string destinationFilePath,
        DownloadOptions options,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var fullDestinationPath = Path.GetFullPath(destinationFilePath);
        var tempFilePath = BuildTempFilePath(fullDestinationPath);
        var metadataFilePath = BuildMetadataFilePath(fullDestinationPath);
        PrepareDestination(fullDestinationPath);

        var probe = await ProbeRemoteFileAsync(sourceUri, cancellationToken);
        var totalBytes = probe.TotalBytes ?? options.ExpectedSizeBytes;
        if (CanReuseCompletedDestination(fullDestinationPath, totalBytes))
        {
            progress?.Report(new DownloadProgressInfo(
                totalBytes ?? new FileInfo(fullDestinationPath).Length,
                totalBytes,
                1d,
                false,
                false));
            CleanupPartialArtifacts(tempFilePath, metadataFilePath);
            return new DownloadResult(true, fullDestinationPath, null, false, false);
        }

        var canUseParallel = probe.SupportsRanges &&
                             totalBytes is > 0 &&
                             totalBytes.Value >= options.ParallelThresholdBytes &&
                             options.MaxParallelSegments > 1;

        try
        {
            var result = canUseParallel
                ? await DownloadRemoteInParallelAsync(
                    sourceUri,
                    fullDestinationPath,
                    tempFilePath,
                    metadataFilePath,
                    totalBytes!.Value,
                    options,
                    progress,
                    cancellationToken)
                : await DownloadRemoteSequentiallyAsync(
                    sourceUri,
                    fullDestinationPath,
                    tempFilePath,
                    metadataFilePath,
                    totalBytes,
                    probe.SupportsRanges,
                    options,
                    progress,
                    cancellationToken);

            return result;
        }
        catch (RangeRequestNotSupportedException)
        {
            ResetPartialArtifacts(tempFilePath, metadataFilePath);
            return await DownloadRemoteSequentiallyAsync(
                sourceUri,
                fullDestinationPath,
                tempFilePath,
                metadataFilePath,
                totalBytes,
                allowResume: false,
                options,
                progress,
                cancellationToken);
        }
    }

    private async Task<DownloadResult> DownloadRemoteSequentiallyAsync(
        Uri sourceUri,
        string destinationFilePath,
        string tempFilePath,
        string metadataFilePath,
        long? totalBytes,
        bool allowResume,
        DownloadOptions options,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        long existingBytes = 0;
        if (File.Exists(tempFilePath))
        {
            existingBytes = new FileInfo(tempFilePath).Length;
            if (totalBytes is > 0 && existingBytes > totalBytes.Value)
            {
                ResetPartialArtifacts(tempFilePath, metadataFilePath);
                existingBytes = 0;
            }
        }

        if (!allowResume && existingBytes > 0)
        {
            ResetPartialArtifacts(tempFilePath, metadataFilePath);
            existingBytes = 0;
        }

        if (totalBytes is > 0 && existingBytes >= totalBytes.Value)
        {
            CompleteDownload(tempFilePath, destinationFilePath, metadataFilePath);
            progress?.Report(new DownloadProgressInfo(totalBytes.Value, totalBytes, 1d, existingBytes > 0, false));
            return new DownloadResult(true, destinationFilePath, null, existingBytes > 0, false);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
        if (allowResume && existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (allowResume && existingBytes > 0)
        {
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && totalBytes is > 0 && existingBytes == totalBytes)
            {
                CompleteDownload(tempFilePath, destinationFilePath, metadataFilePath);
                progress?.Report(new DownloadProgressInfo(totalBytes.Value, totalBytes, 1d, true, false));
                return new DownloadResult(true, destinationFilePath, null, true, false);
            }

            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                throw new RangeRequestNotSupportedException("The server did not honor the resume range request.");
            }
        }

        response.EnsureSuccessStatusCode();

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destinationStream = new FileStream(
            tempFilePath,
            existingBytes > 0 ? FileMode.Open : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            options.BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (existingBytes > 0)
        {
            destinationStream.Seek(existingBytes, SeekOrigin.Begin);
        }

        var effectiveTotalBytes = totalBytes;
        if (effectiveTotalBytes is null && response.Content.Headers.ContentLength is > 0)
        {
            effectiveTotalBytes = existingBytes + response.Content.Headers.ContentLength.Value;
        }

        await CopyStreamAsync(
            sourceStream,
            destinationStream,
            existingBytes,
            effectiveTotalBytes,
            isResuming: existingBytes > 0,
            isParallel: false,
            options.BufferSize,
            progress,
            cancellationToken);

        CompleteDownload(tempFilePath, destinationFilePath, metadataFilePath);
        return new DownloadResult(true, destinationFilePath, null, existingBytes > 0, false);
    }

    private async Task<DownloadResult> DownloadRemoteInParallelAsync(
        Uri sourceUri,
        string destinationFilePath,
        string tempFilePath,
        string metadataFilePath,
        long totalBytes,
        DownloadOptions options,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var requestedSegments = Math.Min(options.MaxParallelSegments, CalculateRecommendedSegments(totalBytes));
        var metadata = await LoadOrCreateMetadataAsync(
            sourceUri,
            tempFilePath,
            metadataFilePath,
            totalBytes,
            requestedSegments,
            cancellationToken);

        await using (var tempStream = new FileStream(
                         tempFilePath,
                         FileMode.OpenOrCreate,
                         FileAccess.Write,
                         FileShare.ReadWrite,
                         options.BufferSize,
                         FileOptions.Asynchronous | FileOptions.RandomAccess))
        {
            if (tempStream.Length != totalBytes)
            {
                tempStream.SetLength(totalBytes);
            }
        }

        var initialDownloadedBytes = metadata.Segments.Sum(segment => segment.CompletedBytes);
        ReportProgress(progress, initialDownloadedBytes, totalBytes, initialDownloadedBytes > 0, true);

        if (initialDownloadedBytes >= totalBytes)
        {
            CompleteDownload(tempFilePath, destinationFilePath, metadataFilePath);
            return new DownloadResult(true, destinationFilePath, null, initialDownloadedBytes > 0, true);
        }

        long downloadedBytes = initialDownloadedBytes;
        var metadataWriter = new MetadataWriter(metadataFilePath, metadata);

        try
        {
            var tasks = metadata.Segments
                .Where(segment => segment.CompletedBytes < segment.Length)
                .Select(segment => DownloadSegmentAsync(
                    sourceUri,
                    tempFilePath,
                    segment,
                    options.BufferSize,
                    delta =>
                    {
                        var currentDownloaded = Interlocked.Add(ref downloadedBytes, delta);
                        ReportProgress(progress, currentDownloaded, totalBytes, initialDownloadedBytes > 0, true);
                    },
                    metadataWriter,
                    cancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);
            await metadataWriter.FlushAsync(cancellationToken);
        }
        catch
        {
            await metadataWriter.FlushAsync(cancellationToken);
            throw;
        }

        CompleteDownload(tempFilePath, destinationFilePath, metadataFilePath);
        ReportProgress(progress, totalBytes, totalBytes, initialDownloadedBytes > 0, true);
        return new DownloadResult(true, destinationFilePath, null, initialDownloadedBytes > 0, true);
    }

    private async Task DownloadSegmentAsync(
        Uri sourceUri,
        string tempFilePath,
        DownloadSegmentState segment,
        int bufferSize,
        Action<int> reportDownloadedBytes,
        MetadataWriter metadataWriter,
        CancellationToken cancellationToken)
    {
        var rangeStart = segment.Start + segment.CompletedBytes;
        if (rangeStart > segment.EndInclusive)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
        request.Headers.Range = new RangeHeaderValue(rangeStart, segment.EndInclusive);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new RangeRequestNotSupportedException(
                $"The server returned HTTP {(int)response.StatusCode} for range {rangeStart}-{segment.EndInclusive}.");
        }

        response.EnsureSuccessStatusCode();

        var contentRange = response.Content.Headers.ContentRange;
        if (contentRange?.From != rangeStart || contentRange.To != segment.EndInclusive)
        {
            throw new RangeRequestNotSupportedException("The server returned an unexpected content range.");
        }

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destinationStream = new FileStream(
            tempFilePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        destinationStream.Seek(rangeStart, SeekOrigin.Begin);

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            while (segment.CompletedBytes < segment.Length)
            {
                var remainingBytes = segment.Length - segment.CompletedBytes;
                var readSize = (int)Math.Min(buffer.Length, remainingBytes);
                var read = await sourceStream.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken);
                if (read <= 0)
                {
                    throw new EndOfStreamException(
                        $"Unexpected end of stream while downloading range {segment.Start}-{segment.EndInclusive}.");
                }

                await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                segment.CompletedBytes += read;
                reportDownloadedBytes(read);
                metadataWriter.MarkDirty();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<RemoteProbeResult> ProbeRemoteFileAsync(Uri sourceUri, CancellationToken cancellationToken)
    {
        long? totalBytes = null;
        var supportsRanges = false;

        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, sourceUri);
            using var headResponse = await _httpClient.SendAsync(
                headRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (headResponse.IsSuccessStatusCode)
            {
                totalBytes = headResponse.Content.Headers.ContentLength;
                supportsRanges = headResponse.Headers.AcceptRanges.Any(
                    value => string.Equals(value, "bytes", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch
        {
            // Fall back to a small range probe when HEAD is unsupported or blocked.
        }

        if (supportsRanges && totalBytes is > 0)
        {
            return new RemoteProbeResult(totalBytes, true);
        }

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, sourceUri);
        rangeRequest.Headers.Range = new RangeHeaderValue(0, 0);

        using var rangeResponse = await _httpClient.SendAsync(
            rangeRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (rangeResponse.StatusCode == HttpStatusCode.PartialContent)
        {
            totalBytes = rangeResponse.Content.Headers.ContentRange?.Length ?? totalBytes;
            return new RemoteProbeResult(totalBytes, true);
        }

        rangeResponse.EnsureSuccessStatusCode();
        totalBytes ??= rangeResponse.Content.Headers.ContentLength;
        return new RemoteProbeResult(totalBytes, false);
    }

    private static async Task CopyStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        long initialDownloadedBytes,
        long? totalBytes,
        bool isResuming,
        bool isParallel,
        int bufferSize,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        var downloadedBytes = initialDownloadedBytes;
        try
        {
            ReportProgress(progress, downloadedBytes, totalBytes, isResuming, isParallel);
            while (true)
            {
                var read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloadedBytes += read;
                ReportProgress(progress, downloadedBytes, totalBytes, isResuming, isParallel);
            }

            await destinationStream.FlushAsync(cancellationToken);
            ReportProgress(progress, downloadedBytes, totalBytes, isResuming, isParallel);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ReportProgress(
        IProgress<DownloadProgressInfo>? progress,
        long downloadedBytes,
        long? totalBytes,
        bool isResuming,
        bool isParallel)
    {
        if (progress is null)
        {
            return;
        }

        double normalizedProgress;
        if (totalBytes is > 0)
        {
            normalizedProgress = Math.Clamp(downloadedBytes / (double)totalBytes.Value, 0d, 1d);
        }
        else
        {
            normalizedProgress = 0d;
        }

        progress.Report(new DownloadProgressInfo(
            downloadedBytes,
            totalBytes,
            normalizedProgress,
            isResuming,
            isParallel));
    }

    private static async Task<DownloadMetadata> LoadOrCreateMetadataAsync(
        Uri sourceUri,
        string tempFilePath,
        string metadataFilePath,
        long totalBytes,
        int segmentCount,
        CancellationToken cancellationToken)
    {
        if (File.Exists(metadataFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(metadataFilePath, cancellationToken);
                var metadata = JsonSerializer.Deserialize<SerializableDownloadMetadata>(json);
                if (metadata is not null)
                {
                    var normalizedMetadata = metadata.ToRuntime();
                    if (string.Equals(normalizedMetadata.Source, sourceUri.ToString(), StringComparison.OrdinalIgnoreCase) &&
                        normalizedMetadata.TotalBytes == totalBytes &&
                        normalizedMetadata.Segments.Count > 0)
                    {
                        return normalizedMetadata.Normalize();
                    }
                }
            }
            catch
            {
                // Reset invalid metadata below.
            }
        }

        ResetPartialArtifacts(tempFilePath, metadataFilePath);
        var createdMetadata = DownloadMetadata.Create(sourceUri.ToString(), totalBytes, segmentCount);
        var serialized = JsonSerializer.Serialize(createdMetadata.ToSerializable(), MetadataSerializerOptions);
        await File.WriteAllTextAsync(metadataFilePath, serialized, cancellationToken);
        return createdMetadata;
    }

    private static DownloadOptions NormalizeOptions(DownloadOptions? options)
    {
        var normalized = options ?? new DownloadOptions();
        var maxParallelSegments = Math.Clamp(normalized.MaxParallelSegments, 1, 8);
        var parallelThresholdBytes = Math.Max(1_048_576, normalized.ParallelThresholdBytes);
        var bufferSize = Math.Max(16 * 1024, normalized.BufferSize);
        return normalized with
        {
            MaxParallelSegments = maxParallelSegments,
            ParallelThresholdBytes = parallelThresholdBytes,
            BufferSize = bufferSize
        };
    }

    private static int CalculateRecommendedSegments(long totalBytes)
    {
        if (totalBytes < 16 * 1024 * 1024)
        {
            return 2;
        }

        if (totalBytes < 64 * 1024 * 1024)
        {
            return 4;
        }

        return 6;
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

    private static void CompleteDownload(string tempFilePath, string destinationFilePath, string metadataFilePath)
    {
        if (!File.Exists(tempFilePath))
        {
            return;
        }

        File.Move(tempFilePath, destinationFilePath, overwrite: true);
        if (File.Exists(metadataFilePath))
        {
            File.Delete(metadataFilePath);
        }
    }

    private static void CleanupPartialArtifacts(string tempFilePath, string metadataFilePath)
    {
        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }

        if (File.Exists(metadataFilePath))
        {
            File.Delete(metadataFilePath);
        }
    }

    private static void ResetPartialArtifacts(string tempFilePath, string metadataFilePath)
    {
        CleanupPartialArtifacts(tempFilePath, metadataFilePath);
    }

    private static string BuildTempFilePath(string destinationFilePath) => destinationFilePath + ".part";

    private static string BuildMetadataFilePath(string destinationFilePath) => destinationFilePath + ".part.json";

    private sealed record RemoteProbeResult(long? TotalBytes, bool SupportsRanges);

    private sealed class RangeRequestNotSupportedException : InvalidOperationException
    {
        public RangeRequestNotSupportedException(string message)
            : base(message)
        {
        }
    }

    private sealed class MetadataWriter
    {
        private readonly string _metadataFilePath;
        private readonly DownloadMetadata _metadata;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private long _lastPersistedTickCount;
        private int _dirty;

        public MetadataWriter(string metadataFilePath, DownloadMetadata metadata)
        {
            _metadataFilePath = metadataFilePath;
            _metadata = metadata;
            _lastPersistedTickCount = Environment.TickCount64;
        }

        public void MarkDirty()
        {
            Interlocked.Exchange(ref _dirty, 1);
            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastPersistedTickCount) < 750)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await FlushAsync(CancellationToken.None);
                }
                catch
                {
                    // The final flush still runs on completion/cancellation.
                }
            });
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _dirty, 0) == 0 && File.Exists(_metadataFilePath))
            {
                return;
            }

            await _writeGate.WaitAsync(cancellationToken);
            try
            {
                var json = JsonSerializer.Serialize(_metadata.ToSerializable(), MetadataSerializerOptions);
                await File.WriteAllTextAsync(_metadataFilePath, json, cancellationToken);
                Interlocked.Exchange(ref _lastPersistedTickCount, Environment.TickCount64);
            }
            finally
            {
                _writeGate.Release();
            }
        }
    }

    private sealed class DownloadMetadata
    {
        public string Source { get; init; } = string.Empty;

        public long TotalBytes { get; init; }

        public List<DownloadSegmentState> Segments { get; init; } = [];

        public static DownloadMetadata Create(string source, long totalBytes, int segmentCount)
        {
            var segments = SplitIntoSegments(totalBytes, segmentCount)
                .Select(range => new DownloadSegmentState(range.Start, range.EndInclusive, 0))
                .ToList();

            return new DownloadMetadata
            {
                Source = source,
                TotalBytes = totalBytes,
                Segments = segments
            };
        }

        public DownloadMetadata Normalize()
        {
            foreach (var segment in Segments)
            {
                segment.CompletedBytes = Math.Clamp(segment.CompletedBytes, 0, segment.Length);
            }

            return this;
        }

        public SerializableDownloadMetadata ToSerializable()
        {
            return new SerializableDownloadMetadata
            {
                Source = Source,
                TotalBytes = TotalBytes,
                Segments = Segments
                    .Select(segment => new SerializableDownloadSegment
                    {
                        Start = segment.Start,
                        EndInclusive = segment.EndInclusive,
                        CompletedBytes = segment.CompletedBytes
                    })
                    .ToList()
            };
        }
    }

    private sealed class DownloadSegmentState
    {
        public DownloadSegmentState(long start, long endInclusive, long completedBytes)
        {
            Start = start;
            EndInclusive = endInclusive;
            CompletedBytes = completedBytes;
        }

        public long Start { get; }

        public long EndInclusive { get; }

        public long Length => EndInclusive - Start + 1;

        public long CompletedBytes { get; set; }
    }

    private sealed class SerializableDownloadMetadata
    {
        public string Source { get; init; } = string.Empty;

        public long TotalBytes { get; init; }

        public List<SerializableDownloadSegment> Segments { get; init; } = [];

        public DownloadMetadata ToRuntime()
        {
            return new DownloadMetadata
            {
                Source = Source,
                TotalBytes = TotalBytes,
                Segments = Segments
                    .Select(segment => new DownloadSegmentState(
                        segment.Start,
                        segment.EndInclusive,
                        segment.CompletedBytes))
                    .ToList()
            };
        }
    }

    private sealed class SerializableDownloadSegment
    {
        public long Start { get; init; }

        public long EndInclusive { get; init; }

        public long CompletedBytes { get; init; }
    }

    private static IEnumerable<(long Start, long EndInclusive)> SplitIntoSegments(long totalBytes, int segmentCount)
    {
        if (totalBytes <= 0)
        {
            yield break;
        }

        var normalizedSegmentCount = Math.Max(1, segmentCount);
        var segmentSize = totalBytes / normalizedSegmentCount;
        var remainder = totalBytes % normalizedSegmentCount;
        long start = 0;

        for (var index = 0; index < normalizedSegmentCount; index++)
        {
            var currentSegmentSize = segmentSize + (index < remainder ? 1 : 0);
            if (currentSegmentSize <= 0)
            {
                continue;
            }

            var endInclusive = start + currentSegmentSize - 1;
            yield return (start, endInclusive);
            start = endInclusive + 1;
        }
    }
}
