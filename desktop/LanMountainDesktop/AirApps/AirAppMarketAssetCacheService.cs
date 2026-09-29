using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Services.AirAppMarket;

/// <summary>
/// Local disk cache for AirApp market assets (README markdown and icon images).
/// Cache validity is driven by index refresh: an entry is reused while its source URL and
/// AirApp version are unchanged, and refreshed only when the market index reports a change.
/// </summary>
public sealed class AirAppMarketAssetCacheService : IDisposable
{
    private const string ReadmeKeySuffix = "/readme";

    private const string IconKeySuffix = "/icon";

    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _cacheDirectory;
    private readonly string _readmeDirectory;
    private readonly string _iconsDirectory;
    private readonly string _manifestPath;
    private readonly object _manifestGate = new();
    private AssetCacheManifest _manifest;

    public AirAppMarketAssetCacheService(string airAppMarketDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(airAppMarketDataDirectory);

        _cacheDirectory = Path.Combine(airAppMarketDataDirectory, "cache", "assets");
        _readmeDirectory = Path.Combine(_cacheDirectory, "readme");
        _iconsDirectory = Path.Combine(_cacheDirectory, "icons");
        _manifestPath = Path.Combine(_cacheDirectory, "manifest.json");
        _manifest = LoadManifest();
    }

    /// <summary>
    /// Returns the cached README path for the AirApp when the cache is fresh, or null when it
    /// must be (re)fetched. Callers then download and store via <see cref="StoreReadmeAsync"/>.
    /// </summary>
    public string? TryGetReadme(string airAppId, string sourceUrl, string airAppVersion)
    {
        return TryGetAsset(airAppId, sourceUrl, airAppVersion, AssetKind.Readme, _readmeDirectory, ".md");
    }

    public async Task StoreReadmeAsync(
        string airAppId,
        string sourceUrl,
        string airAppVersion,
        Stream content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_readmeDirectory);
        var path = Path.Combine(_readmeDirectory, SanitizeFileName(airAppId) + ".md");
        await AtomicFileWriter.WriteStreamAsync(path, content, "AirAppMarketCache", cancellationToken)
            .ConfigureAwait(false);
        RecordEntry(airAppId, sourceUrl, airAppVersion, AssetKind.Readme);
    }

    /// <summary>
    /// Returns the cached icon path for the AirApp when the cache is fresh, or null when it
    /// must be (re)fetched.
    /// </summary>
    public string? TryGetIcon(string airAppId, string sourceUrl, string airAppVersion)
    {
        var extension = InferIconExtension(sourceUrl);
        return TryGetAsset(airAppId, sourceUrl, airAppVersion, AssetKind.Icon, _iconsDirectory, extension);
    }

    public async Task StoreIconAsync(
        string airAppId,
        string sourceUrl,
        string airAppVersion,
        Stream content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_iconsDirectory);
        var extension = InferIconExtension(sourceUrl);
        var path = Path.Combine(_iconsDirectory, SanitizeFileName(airAppId) + extension);
        await AtomicFileWriter.WriteStreamAsync(path, content, "AirAppMarketCache", cancellationToken)
            .ConfigureAwait(false);
        RecordEntry(airAppId, sourceUrl, airAppVersion, AssetKind.Icon);
    }

    /// <summary>
    /// Removes the cached assets for a AirApp (for example after an uninstall).
    /// </summary>
    public void Invalidate(string airAppId)
    {
        lock (_manifestGate)
        {
            var removedReadme = _manifest.Entries.Remove(BuildEntryKey(airAppId, AssetKind.Readme));
            var removedIcon = _manifest.Entries.Remove(BuildEntryKey(airAppId, AssetKind.Icon));
            if (!removedReadme && !removedIcon)
            {
                return;
            }
        }

        TryDelete(Path.Combine(_readmeDirectory, SanitizeFileName(airAppId) + ".md"));
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp" })
        {
            TryDelete(Path.Combine(_iconsDirectory, SanitizeFileName(airAppId) + extension));
        }

        SaveManifest();
    }

    /// <summary>
    /// Clears every cached asset and the manifest.
    /// </summary>
    public void ClearAll()
    {
        lock (_manifestGate)
        {
            _manifest = new AssetCacheManifest();
        }

        FileOperationRetryHelper.TryDeleteDirectory(_readmeDirectory, true, "AirAppMarket");
        FileOperationRetryHelper.TryDeleteDirectory(_iconsDirectory, true, "AirAppMarket");
        SaveManifest();
    }

    public void Dispose()
    {
        SaveManifest();
    }

    private string? TryGetAsset(
        string airAppId,
        string sourceUrl,
        string airAppVersion,
        AssetKind assetKind,
        string directory,
        string extension)
    {
        lock (_manifestGate)
        {
            if (!_manifest.Entries.TryGetValue(BuildEntryKey(airAppId, assetKind), out var entry))
            {
                return null;
            }

            if (entry.AssetKind != assetKind)
            {
                return null;
            }

            if (!string.Equals(entry.SourceUrl, sourceUrl, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.AirAppVersion, airAppVersion, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var path = Path.Combine(directory, SanitizeFileName(airAppId) + extension);
        return File.Exists(path) ? path : null;
    }

    private void RecordEntry(string airAppId, string sourceUrl, string airAppVersion, AssetKind assetKind)
    {
        lock (_manifestGate)
        {
            _manifest.Entries[BuildEntryKey(airAppId, assetKind)] = new AssetCacheEntry(
                assetKind,
                sourceUrl,
                airAppVersion,
                DateTimeOffset.UtcNow);
        }

        SaveManifest();
    }

    /// <summary>
    /// 清单的键是 <c>{appId}/readme</c> 与 <c>{appId}/icon</c> 两条，不是一个 appId 一条。
    /// </summary>
    /// <remarks>
    /// 改这一处之前，同一个应用的 README 与图标在清单里互相覆盖：存完图标再问 README 就得到
    /// "没缓存"，于是每次打开详情都重下一遍 README，而那个 <c>.md</c> 早就静静躺在盘上
    /// （文件是分目录存的，所以盘上没坏，坏的是"新鲜与否"这条记录）。
    /// 键里带种类之后 <see cref="LoadManifest"/> 会把读不出种类的旧键丢掉——这是缓存，
    /// 丢掉的代价是那一份资产重下一次，比留着一条永远对不上的旧记录便宜。
    /// </remarks>
    private static string BuildEntryKey(string airAppId, AssetKind assetKind) =>
        $"{airAppId}{(assetKind == AssetKind.Readme ? ReadmeKeySuffix : IconKeySuffix)}";

    private static bool IsEntryKeyOfKind(string key, string suffix) =>
        key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
        key.Length > suffix.Length;

    private AssetCacheManifest LoadManifest()
    {
        try
        {
            if (!File.Exists(_manifestPath))
            {
                return new AssetCacheManifest();
            }

            var json = File.ReadAllText(_manifestPath);
            var loaded = JsonSerializer.Deserialize<AssetCacheManifest>(json, ManifestSerializerOptions)
                ?? new AssetCacheManifest();

            // 旧格式（一个 appId 一条记录，键里不带种类）读进来直接丢掉：那类记录对不上任何一次查询，
            // 留着只会让"这条资产到底缓存了什么"在文件里多留一份没人认领的旧事实。
            // 丢掉的实际代价是那份资产重下一次——缓存重建得起，读错代价不该由正确性付。
            foreach (var key in loaded.Entries.Keys
                         .Where(k => !IsEntryKeyOfKind(k, ReadmeKeySuffix) && !IsEntryKeyOfKind(k, IconKeySuffix))
                         .ToArray())
            {
                loaded.Entries.Remove(key);
            }

            return loaded;
        }
        catch
        {
            return new AssetCacheManifest();
        }
    }

    private void SaveManifest()
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            AssetCacheManifest snapshot;
            lock (_manifestGate)
            {
                snapshot = _manifest;
            }

            var json = JsonSerializer.Serialize(snapshot, ManifestSerializerOptions);
            AtomicFileWriter.WriteText(_manifestPath, json, "AirAppMarketCache");
        }
        catch
        {
            // Cache persistence is best-effort; never fail the asset load because of it.
        }
    }

    private static string InferIconExtension(string sourceUrl)
    {
        try
        {
            var uri = new Uri(sourceUrl, UriKind.Absolute);
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return extension.ToLowerInvariant();
            }
        }
        catch
        {
            // Ignore malformed URLs; default below.
        }

        return ".png";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Create(value.Length, value, (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                span[i] = invalid.Contains(src[i]) ? '_' : src[i];
            }
        });
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
        catch
        {
            // Best-effort cleanup.
        }
    }

    private enum AssetKind
    {
        Readme = 0,
        Icon = 1
    }

    private sealed class AssetCacheManifest
    {
        [JsonPropertyName("entries")]
        public Dictionary<string, AssetCacheEntry> Entries { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AssetCacheEntry
    {
        public AssetCacheEntry()
        {
        }

        public AssetCacheEntry(AssetKind assetKind, string sourceUrl, string airAppVersion, DateTimeOffset cachedAt)
        {
            AssetKind = assetKind;
            SourceUrl = sourceUrl;
            AirAppVersion = airAppVersion;
            CachedAt = cachedAt;
        }

        [JsonPropertyName("assetKind")]
        public AssetKind AssetKind { get; init; }

        [JsonPropertyName("sourceUrl")]
        public string SourceUrl { get; init; } = string.Empty;

        [JsonPropertyName("pluginVersion")]
        public string AirAppVersion { get; init; } = string.Empty;

        [JsonPropertyName("cachedAt")]
        public DateTimeOffset CachedAt { get; init; }
    }
}
