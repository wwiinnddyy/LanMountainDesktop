using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

public sealed record ZhiJiaoHubLocalImageItem(
    string Name,
    string OriginalUrl,
    string LocalPath,
    int Index);

public sealed record ZhiJiaoHubLocalSnapshot(
    IReadOnlyList<ZhiJiaoHubLocalImageItem> Images,
    string Source,
    DateTimeOffset LastUpdated,
    int TotalCount);

public sealed record ZhiJiaoHubSyncResult(
    bool Success,
    ZhiJiaoHubLocalSnapshot? Snapshot,
    int DownloadedCount,
    int SkippedCount,
    int FailedCount,
    string? ErrorMessage = null);

public sealed class ZhiJiaoHubCacheService : IDisposable
{
    private static readonly HttpClient DownloadClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _cacheDirectory;
    private readonly string _manifestPath;
    private readonly object _manifestLock = new();
    private bool _isDisposed;

    static ZhiJiaoHubCacheService()
    {
        var handler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        DownloadClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        DownloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop/1.0");
    }

    public ZhiJiaoHubCacheService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDirectory = Path.Combine(appData, "LanMountainDesktop", "cache", "zhijiaohub");
        _cacheDirectory = dataDirectory;
        _manifestPath = Path.Combine(dataDirectory, "manifest.json");
    }

    public string CacheDirectory => _cacheDirectory;

    public bool HasLocalCache(string source)
    {
        lock (_manifestLock)
        {
            if (!File.Exists(_manifestPath))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(_manifestPath);
                var manifest = JsonSerializer.Deserialize<CacheManifest>(json, JsonOptions);
                return manifest?.Entries?.ContainsKey(source) == true &&
                       manifest.Entries[source].Images.Count > 0 &&
                       Directory.Exists(GetSourceDirectory(source));
            }
            catch
            {
                return false;
            }
        }
    }

    public ZhiJiaoHubLocalSnapshot? LoadLocalSnapshot(string source)
    {
        lock (_manifestLock)
        {
            if (!File.Exists(_manifestPath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(_manifestPath);
                var manifest = JsonSerializer.Deserialize<CacheManifest>(json, JsonOptions);
                if (manifest?.Entries?.TryGetValue(source, out var entry) != true)
                {
                    return null;
                }

                var sourceDir = GetSourceDirectory(source);
                var images = entry.Images
                    .Where(img => File.Exists(Path.Combine(sourceDir, img.LocalFileName)))
                    .Select((img, idx) => new ZhiJiaoHubLocalImageItem(
                        img.Name,
                        img.OriginalUrl,
                        Path.Combine(sourceDir, img.LocalFileName),
                        idx))
                    .ToList();

                if (images.Count == 0)
                {
                    return null;
                }

                return new ZhiJiaoHubLocalSnapshot(
                    images,
                    source,
                    entry.LastUpdated,
                    images.Count);
            }
            catch
            {
                return null;
            }
        }
    }

    public async Task<ZhiJiaoHubSyncResult> SyncImagesAsync(
        string source,
        IReadOnlyList<ZhiJiaoHubImageItem> remoteImages,
        string mirrorSource,
        IProgress<(int Current, int Total, string Status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (remoteImages == null || remoteImages.Count == 0)
        {
            return new ZhiJiaoHubSyncResult(false, null, 0, 0, 0, "No images to sync");
        }

        var sourceDir = GetSourceDirectory(source);
        Directory.CreateDirectory(sourceDir);

        var downloadedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        var localImages = new List<CachedImageInfo>();

        var existingFiles = new HashSet<string>(
            Directory.Exists(sourceDir)
                ? Directory.GetFiles(sourceDir, "*.jpg").Concat(Directory.GetFiles(sourceDir, "*.png")).Concat(Directory.GetFiles(sourceDir, "*.gif"))
                : Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < remoteImages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remoteImage = remoteImages[i];
            var fileName = GetSafeFileName(remoteImage.Name, remoteImage.Url);
            var localPath = Path.Combine(sourceDir, fileName);

            progress?.Report((i + 1, remoteImages.Count, $"Downloading {remoteImage.Name}..."));

            if (File.Exists(localPath))
            {
                skippedCount++;
                localImages.Add(new CachedImageInfo(remoteImage.Name, remoteImage.Url, fileName));
                continue;
            }

            try
            {
                var downloadUrl = ResolveDownloadUrl(remoteImage.Url, mirrorSource);
                using var response = await DownloadClient.GetAsync(downloadUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var fileStream = File.Create(localPath);
                await response.Content.CopyToAsync(fileStream, cancellationToken);

                downloadedCount++;
                localImages.Add(new CachedImageInfo(remoteImage.Name, remoteImage.Url, fileName));
            }
            catch (Exception)
            {
                failedCount++;
            }
        }

        if (localImages.Count == 0)
        {
            return new ZhiJiaoHubSyncResult(false, null, downloadedCount, skippedCount, failedCount, "All downloads failed");
        }

        SaveManifest(source, localImages);

        var snapshot = new ZhiJiaoHubLocalSnapshot(
            localImages.Select((img, idx) => new ZhiJiaoHubLocalImageItem(
                img.Name,
                img.OriginalUrl,
                Path.Combine(sourceDir, img.LocalFileName),
                idx)).ToList(),
            source,
            DateTimeOffset.UtcNow,
            localImages.Count);

        return new ZhiJiaoHubSyncResult(true, snapshot, downloadedCount, skippedCount, failedCount);
    }

    public void ClearCache(string? source = null)
    {
        lock (_manifestLock)
        {
            if (source != null)
            {
                var sourceDir = GetSourceDirectory(source);
                if (Directory.Exists(sourceDir))
                {
                    Directory.Delete(sourceDir, true);
                }

                if (File.Exists(_manifestPath))
                {
                    try
                    {
                        var json = File.ReadAllText(_manifestPath);
                        var manifest = JsonSerializer.Deserialize<CacheManifest>(json, JsonOptions);
                        if (manifest?.Entries != null && manifest.Entries.ContainsKey(source))
                        {
                            manifest.Entries.Remove(source);
                            File.WriteAllText(_manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
                        }
                    }
                    catch
                    {
                    }
                }
            }
            else
            {
                if (Directory.Exists(_cacheDirectory))
                {
                    Directory.Delete(_cacheDirectory, true);
                }
            }
        }
    }

    private string GetSourceDirectory(string source)
    {
        return Path.Combine(_cacheDirectory, source.ToLowerInvariant().Replace(" ", "-"));
    }

    private static string GetSafeFileName(string name, string url)
    {
        var ext = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(ext) || ext.Length > 5)
        {
            ext = ".jpg";
        }

        var safeName = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = Guid.NewGuid().ToString("N")[..8];
        }

        return $"{safeName}{ext}";
    }

    private static string ResolveDownloadUrl(string originalUrl, string mirrorSource)
    {
        if (string.Equals(mirrorSource, ZhiJiaoHubMirrorSources.GhProxy, StringComparison.OrdinalIgnoreCase))
        {
            return ZhiJiaoHubMirrorSources.GhProxyBaseUrl.TrimEnd('/') + "/" + originalUrl;
        }

        return originalUrl;
    }

    private void SaveManifest(string source, List<CachedImageInfo> images)
    {
        lock (_manifestLock)
        {
            CacheManifest manifest;
            if (File.Exists(_manifestPath))
            {
                try
                {
                    var json = File.ReadAllText(_manifestPath);
                    manifest = JsonSerializer.Deserialize<CacheManifest>(json, JsonOptions) ?? new CacheManifest();
                }
                catch
                {
                    manifest = new CacheManifest();
                }
            }
            else
            {
                manifest = new CacheManifest();
            }

            manifest.Entries[source] = new CacheEntry(images, DateTimeOffset.UtcNow);

            Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
            File.WriteAllText(_manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
    }

    private sealed class CacheManifest
    {
        public Dictionary<string, CacheEntry> Entries { get; set; } = new();
    }

    private sealed class CacheEntry
    {
        public List<CachedImageInfo> Images { get; set; }
        public DateTimeOffset LastUpdated { get; set; }

        public CacheEntry(List<CachedImageInfo> images, DateTimeOffset lastUpdated)
        {
            Images = images;
            LastUpdated = lastUpdated;
        }
    }

    private sealed class CachedImageInfo
    {
        public string Name { get; set; }
        public string OriginalUrl { get; set; }
        public string LocalFileName { get; set; }

        public CachedImageInfo(string name, string originalUrl, string localFileName)
        {
            Name = name;
            OriginalUrl = originalUrl;
            LocalFileName = localFileName;
        }
    }
}
