using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LanDesktopPLONDS.Installer.Models;
using LanMountainDesktop.Shared.Contracts.Deployment;

namespace LanDesktopPLONDS.Installer.Services;

internal sealed class InstallerPlondsClient
{
    private const string S3ManifestUrlEnvironmentVariable = "LANMOUNTAIN_PLONDS_S3_MANIFEST_URL";
    private const string GitHubManifestUrlEnvironmentVariable = "LANMOUNTAIN_PLONDS_GITHUB_MANIFEST_URL";
    private const string DefaultS3ManifestUrl = "https://cn-nb1.rains3.com/lmdesktop/lanmountain/update/plonds/PLONDS.json";
    private const string DefaultGitHubManifestUrl = "https://github.com/wwiinnddyy/LanMountainDesktop/releases/latest/download/PLONDS.json";

    /// <summary>下载最大重试次数（每 URL）。</summary>
    internal const int MaxDownloadRetries = 3;

    /// <summary>Manifest 请求超时（秒）。</summary>
    internal const int ManifestFetchTimeoutSeconds = 10;

    /// <summary>暂存空间倍数：需要至少 2 倍估算包大小。</summary>
    private const long RequiredSpaceMultiplier = 2;

    private readonly HttpClient _httpClient;
    private readonly string _stagingRoot;
    private readonly Func<int, TimeSpan>? _retryDelayFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// 生产构造函数。
    /// </summary>
    public InstallerPlondsClient(HttpClient httpClient, string stagingRoot)
        : this(httpClient, stagingRoot, null)
    {
    }

    /// <summary>
    /// 内部构造函数，允许注入重试延迟策略（用于测试）。
    /// </summary>
    internal InstallerPlondsClient(HttpClient httpClient, string stagingRoot, Func<int, TimeSpan>? retryDelayFactory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _stagingRoot = stagingRoot ?? throw new ArgumentNullException(nameof(stagingRoot));
        _retryDelayFactory = retryDelayFactory;
    }

    public static IReadOnlyList<InstallerPlondsSource> CreateBuiltInSources()
    {
        return
        [
            new("s3", "s3", ResolveManifestUrl(S3ManifestUrlEnvironmentVariable, DefaultS3ManifestUrl), 100),
            new("github", "github", ResolveManifestUrl(GitHubManifestUrlEnvironmentVariable, DefaultGitHubManifestUrl), 50)
        ];
    }

    /// <summary>
    /// 查找最新可用的 PLONDS 全量包源。
    /// 诊断聚合：记录所有源探测失败信息，当无可用源时抛出包含所有错误的中文异常。
    /// </summary>
    public async Task<InstallerPlondsCandidate> FindLatestAsync(CancellationToken cancellationToken)
    {
        var sources = CreateBuiltInSources().ToList();
        var candidates = new List<InstallerPlondsCandidate>();
        var probeReport = new InstallerSourceProbeReport();

        for (var index = 0; index < sources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = sources[index];
            InstallerPlondsManifest manifest;
            try
            {
                manifest = await GetManifestAsync(source, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                probeReport.AddFailure(source.Id, source.ManifestUrl, ex);
                continue;
            }

            AddManifestSources(sources, manifest.Sources);
            var filesUrl = InstallerPlondsUrlResolver.ResolveFilesZipUrls(manifest, source).FirstOrDefault();
            if (filesUrl is null)
            {
                probeReport.AddFailure(source.Id, source.ManifestUrl, "清单中未找到可用的 Files.zip 下载链接。");
                continue;
            }

            candidates.Add(new InstallerPlondsCandidate(source, manifest, filesUrl));
        }

        var bestCandidate = candidates
            .Where(candidate => SemanticVersion.TryParse(candidate.Manifest.CurrentVersion, out _))
            .OrderByDescending(candidate => SemanticVersion.Parse(candidate.Manifest.CurrentVersion))
            .ThenByDescending(candidate => candidate.Source.Priority)
            .FirstOrDefault();

        if (bestCandidate is not null)
        {
            return bestCandidate;
        }

        // 所有源均不可用，抛出包含所有错误的中文异常
        if (probeReport.HasFailures)
        {
            throw new InvalidOperationException(probeReport.FormatChineseSummary());
        }

        throw new InvalidOperationException("未找到可用的 PLONDS 全量包源。");
    }

    /// <summary>
    /// 下载并准备全量包。支持：重试（指数退避）、HTTP Range 续传、停滞检测、暂存空间检查。
    /// 保持与现有调用方源兼容的签名。
    /// </summary>
    public async Task<PreparedFilesPackage> DownloadAndPrepareFullPackageAsync(
        InstallerPlondsCandidate candidate,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        var version = SemanticVersion.Parse(candidate.Manifest.CurrentVersion).ToString();
        var packageRoot = Path.Combine(_stagingRoot, SanitizePathSegment(version), SanitizePathSegment(candidate.Source.Id), "full");
        var urls = new[] { candidate.FilesZipUrl }
            .Concat(InstallerPlondsUrlResolver.ResolveFilesZipUrls(candidate.Manifest, candidate.Source))
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // 暂存空间检查：需要至少 2 倍估算包大小
        EnsureStagingSpaceAvailable(candidate.Manifest, packageRoot);

        Exception? lastError = null;

        foreach (var filesZipUrl in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 清理上一个 URL 的残留，但保留 .partial 文件以支持续传
            if (Directory.Exists(packageRoot))
            {
                var partialPath = Path.Combine(packageRoot, "Files.zip.partial");
                var hasPartial = File.Exists(partialPath);
                var hasFinal = File.Exists(Path.Combine(packageRoot, "Files.zip"));

                // 切换 URL 时清理已完成的文件，保留 .partial
                if (hasFinal || !hasPartial)
                {
                    Directory.Delete(packageRoot, recursive: true);
                }
            }

            Directory.CreateDirectory(packageRoot);
            var zipPath = Path.Combine(packageRoot, "Files.zip");
            var extractDirectory = Path.Combine(packageRoot, "Files");
            Directory.CreateDirectory(extractDirectory);
            var attempt = candidate with { FilesZipUrl = filesZipUrl };

            // 带指数退避的重试循环
            for (var retryAttempt = 0; retryAttempt < MaxDownloadRetries; retryAttempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await DownloadWithRetryAsync(attempt, zipPath, progress, cancellationToken).ConfigureAwait(false);
                    await VerifyPackageAsync(zipPath, attempt.Manifest, filesZipUrl, cancellationToken).ConfigureAwait(false);
                    ExtractZip(zipPath, extractDirectory);

                    progress?.Report(new InstallerDeployProgress(
                        "Files package prepared",
                        version,
                        1,
                        0.10,
                        "Files.zip",
                        new FileInfo(zipPath).Length,
                        new FileInfo(zipPath).Length));

                    return new PreparedFilesPackage(version, candidate.Source.Id, zipPath, extractDirectory, candidate.Manifest);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;

                    // 指数退避（最后一次重试不再等待）
                    if (retryAttempt < MaxDownloadRetries - 1)
                    {
                        var delay = _retryDelayFactory is not null
                            ? _retryDelayFactory(retryAttempt)
                            : TimeSpan.FromSeconds(Math.Pow(2, retryAttempt + 1)); // 2s, 4s
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        throw new InvalidOperationException("下载并准备 PLONDS Files 包失败。", lastError);
    }

    /// <summary>估算安装所需的字节数。</summary>
    public static long EstimateInstallBytes(InstallerPlondsManifest manifest)
    {
        var filesBytes = manifest.FilesMap?.Values.Sum(file => Math.Max(0, file.Size)) ?? 0;
        var packageBytes = FindChecksumSizeHint(manifest.Checksums);
        return Math.Max(filesBytes, packageBytes);
    }

    /// <summary>
    /// 使用 10 秒超时获取清单。
    /// </summary>
    private async Task<InstallerPlondsManifest> GetManifestAsync(
        InstallerPlondsSource source,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(ManifestFetchTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var response = await _httpClient.GetAsync(source.ManifestUrl, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync(stream, InstallerJsonContext.Default.InstallerPlondsManifest, linkedCts.Token)
            .ConfigureAwait(false);

        return manifest ?? throw new InvalidOperationException($"清单反序列化结果为空（源: {source.Id}）。");
    }

    /// <summary>
    /// 使用 ResilientDownloader 执行单次下载流程（含重试外层由调用方控制）。
    /// </summary>
    private async Task DownloadWithRetryAsync(
        InstallerPlondsCandidate candidate,
        string zipPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 获取文件大小用于进度报告
        long totalBytes = 0;
        using (var headRequest = new HttpRequestMessage(HttpMethod.Head, candidate.FilesZipUrl))
        {
            try
            {
                using var headResponse = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                totalBytes = headResponse.Content.Headers.ContentLength ?? 0;
            }
            catch
            {
                // HEAD 请求失败不影响下载，使用 0 作为总大小
            }
        }

        // 包装进度回调：传递版本信息
        var wrappedProgress = new Progress<long>(downloaded =>
        {
            var fraction = totalBytes > 0 ? Math.Clamp((double)downloaded / totalBytes, 0, 1) : 0;
            progress?.Report(new InstallerDeployProgress(
                "Downloading Files.zip",
                candidate.Manifest.CurrentVersion,
                fraction,
                0,
                "Files.zip",
                downloaded,
                totalBytes));
        });

        await ResilientDownloader.DownloadSingleAttemptAsync(
            _httpClient,
            candidate.FilesZipUrl,
            zipPath,
            wrappedProgress,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查暂存目录所在磁盘是否有足够空间（至少 2 倍估算包大小）。
    /// 若估算大小为 0 则跳过检查。
    /// </summary>
    private void EnsureStagingSpaceAvailable(InstallerPlondsManifest manifest, string packageRoot)
    {
        var estimatedBytes = EstimateInstallBytes(manifest);
        if (estimatedBytes <= 0)
        {
            return;
        }

        var requiredBytes = estimatedBytes * RequiredSpaceMultiplier;
        try
        {
            // 确保暂存目录的父目录存在以获取驱动器信息
            var parentDir = Path.GetDirectoryName(Path.GetFullPath(packageRoot));
            if (string.IsNullOrEmpty(parentDir))
            {
                return;
            }

            var driveRoot = Path.GetPathRoot(parentDir);
            if (string.IsNullOrEmpty(driveRoot))
            {
                return;
            }

            var driveInfo = new DriveInfo(driveRoot);
            if (driveInfo.AvailableFreeSpace > 0 && driveInfo.AvailableFreeSpace < requiredBytes)
            {
                throw new InvalidOperationException(
                    $"暂存目录可用空间不足。需要至少 {FormatBytes(requiredBytes)}，" +
                    $"当前可用 {FormatBytes(driveInfo.AvailableFreeSpace)}。暂存路径: {_stagingRoot}");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            // 无法检测磁盘空间时跳过检查（如网络路径）
        }
    }

    private static void ExtractZip(string zipPath, string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }

        Directory.CreateDirectory(destinationDirectory);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var normalizedName = InstallerPathGuard.NormalizeRelativePath(entry.FullName);
            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, normalizedName));
            InstallerPathGuard.EnsureChildPath(destinationDirectory, destinationPath);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void AddManifestSources(List<InstallerPlondsSource> sources, IEnumerable<InstallerPlondsSource>? manifestSources)
    {
        if (manifestSources is null)
        {
            return;
        }

        foreach (var source in manifestSources)
        {
            if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.ManifestUrl))
            {
                continue;
            }

            if (sources.Any(existing => string.Equals(existing.Id, source.Id, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(existing.ManifestUrl, source.ManifestUrl, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            sources.Add(source with
            {
                Id = source.Id.Trim(),
                Kind = string.IsNullOrWhiteSpace(source.Kind) ? "http" : source.Kind.Trim(),
                ManifestUrl = source.ManifestUrl.Trim()
            });
        }
    }

    private static IReadOnlyList<string> GetChecksumKeys(Uri url)
    {
        var urlFileName = Path.GetFileName(url.LocalPath);
        return new[] { "Files.zip", "files.zip", "files-windows-x64.zip", urlFileName }
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FindChecksum(IReadOnlyDictionary<string, string>? checksums, IEnumerable<string> keys)
    {
        if (checksums is null || checksums.Count == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (checksums.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var match = checksums.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// 校验和解析：仅接受 SHA-256；MD5 拒绝并抛出清晰的中文错误。
    /// 兼容 "sha256:HEX" 和纯 64 位十六进制格式。
    /// </summary>
    private static (string Algorithm, string Hash) ParseChecksum(string checksum)
    {
        var normalized = checksum.Trim();
        var separatorIndex = normalized.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            var algorithm = normalized[..separatorIndex].Trim().ToLowerInvariant();
            var hash = NormalizeHash(normalized[(separatorIndex + 1)..]);

            if (algorithm == "md5")
            {
                throw new InvalidDataException("MD5 校验和不被支持，请使用 SHA-256 校验和。");
            }

            if (algorithm == "sha256" && hash.Length > 0)
            {
                return (algorithm, hash);
            }
        }

        var inferred = NormalizeHash(normalized);
        return inferred.Length switch
        {
            // 32 位十六进制 = MD5，明确拒绝
            32 => throw new InvalidDataException("检测到 MD5 校验和（32 位十六进制），但 MD5 不被支持。请使用 SHA-256 校验和。"),
            64 => ("sha256", inferred),
            _ => throw new InvalidDataException($"不支持的校验和格式: {checksum}")
        };
    }

    private static async Task VerifyPackageAsync(
        string zipPath,
        InstallerPlondsManifest manifest,
        Uri filesZipUrl,
        CancellationToken cancellationToken)
    {
        var checksum = FindChecksum(manifest.Checksums, GetChecksumKeys(filesZipUrl));
        if (checksum is null)
        {
            throw new InvalidDataException("PLONDS 清单中未声明 Files.zip 的校验和。");
        }

        var (algorithm, expectedHash) = ParseChecksum(checksum);
        var actualHash = await ComputeHashAsync(zipPath, algorithm, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"PLONDS Files.zip 校验和不匹配。期望 {algorithm}:{expectedHash}，实际 {algorithm}:{actualHash}。");
        }
    }

    /// <summary>
    /// 计算文件哈希：仅支持 SHA-256。
    /// </summary>
    private static async Task<string> ComputeHashAsync(string filePath, string algorithm, CancellationToken cancellationToken)
    {
        if (algorithm != "sha256")
        {
            throw new InvalidDataException($"不支持的校验和算法: {algorithm}，仅支持 SHA-256。");
        }

        using var sha = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static long FindChecksumSizeHint(IReadOnlyDictionary<string, string>? checksums)
    {
        _ = checksums;
        return 0;
    }

    private static string NormalizeHash(string value)
    {
        return value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    private static string ResolveManifestUrl(string environmentVariable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }

        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / (1024.0 * 1024):F0} MB";
        }

        return $"{bytes / 1024.0:F0} KB";
    }
}
