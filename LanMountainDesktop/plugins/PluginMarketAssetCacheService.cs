using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services.PluginMarket;

/// <summary>
/// Local disk cache for plugin market assets (README markdown and icon images).
/// Cache validity is driven by index refresh: an entry is reused while its source URL and
/// plugin version are unchanged, and refreshed only when the market index reports a change.
/// </summary>
public sealed class PluginMarketAssetCacheService : IDisposable
{
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

    public PluginMarketAssetCacheService(string pluginMarketDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginMarketDataDirectory);

        _cacheDirectory = Path.Combine(pluginMarketDataDirectory, "cache", "assets");
        _readmeDirectory = Path.Combine(_cacheDirectory, "readme");
        _iconsDirectory = Path.Combine(_cacheDirectory, "icons");
        _manifestPath = Path.Combine(_cacheDirectory, "manifest.json");
        _manifest = LoadManifest();
    }

    /// <summary>
    /// Returns the cached README path for the plugin when the cache is fresh, or null when it
    /// must be (re)fetched. Callers then download and store via <see cref="StoreReadmeAsync"/>.
    /// </summary>
    public string? TryGetReadme(string pluginId, string sourceUrl, string pluginVersion)
    {
        return TryGetAsset(pluginId, sourceUrl, pluginVersion, "readme", _readmeDirectory, ".md");
    }

    public async Task StoreReadmeAsync(
        string pluginId,
        string sourceUrl,
        string pluginVersion,
        Stream content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_readmeDirectory);
        var path = Path.Combine(_readmeDirectory, SanitizeFileName(pluginId) + ".md");
        await WriteAtomicallyAsync(path, content, cancellationToken).ConfigureAwait(false);
        RecordEntry(pluginId, sourceUrl, pluginVersion, AssetKind.Readme);
    }

    /// <summary>
    /// Returns the cached icon path for the plugin when the cache is fresh, or null when it
    /// must be (re)fetched.
    /// </summary>
    public string? TryGetIcon(string pluginId, string sourceUrl, string pluginVersion)
    {
        var extension = InferIconExtension(sourceUrl);
        return TryGetAsset(pluginId, sourceUrl, pluginVersion, "icon", _iconsDirectory, extension);
    }

    public async Task StoreIconAsync(
        string pluginId,
        string sourceUrl,
        string pluginVersion,
        Stream content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_iconsDirectory);
        var extension = InferIconExtension(sourceUrl);
        var path = Path.Combine(_iconsDirectory, SanitizeFileName(pluginId) + extension);
        await WriteAtomicallyAsync(path, content, cancellationToken).ConfigureAwait(false);
        RecordEntry(pluginId, sourceUrl, pluginVersion, AssetKind.Icon);
    }

    /// <summary>
    /// Removes the cached assets for a plugin (for example after an uninstall).
    /// </summary>
    public void Invalidate(string pluginId)
    {
        lock (_manifestGate)
        {
            if (!_manifest.Entries.Remove(pluginId))
            {
                return;
            }
        }

        TryDelete(Path.Combine(_readmeDirectory, SanitizeFileName(pluginId) + ".md"));
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp" })
        {
            TryDelete(Path.Combine(_iconsDirectory, SanitizeFileName(pluginId) + extension));
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

        TryDeleteDirectory(_readmeDirectory);
        TryDeleteDirectory(_iconsDirectory);
        SaveManifest();
    }

    public void Dispose()
    {
        SaveManifest();
    }

    private string? TryGetAsset(
        string pluginId,
        string sourceUrl,
        string pluginVersion,
        string assetLabel,
        string directory,
        string extension)
    {
        lock (_manifestGate)
        {
            if (!_manifest.Entries.TryGetValue(pluginId, out var entry))
            {
                return null;
            }

            var expectedAsset = assetLabel == "readme" ? AssetKind.Readme : AssetKind.Icon;
            if (entry.AssetKind != expectedAsset)
            {
                return null;
            }

            if (!string.Equals(entry.SourceUrl, sourceUrl, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.PluginVersion, pluginVersion, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var path = Path.Combine(directory, SanitizeFileName(pluginId) + extension);
        return File.Exists(path) ? path : null;
    }

    private void RecordEntry(string pluginId, string sourceUrl, string pluginVersion, AssetKind assetKind)
    {
        lock (_manifestGate)
        {
            _manifest.Entries[pluginId] = new AssetCacheEntry(
                assetKind,
                sourceUrl,
                pluginVersion,
                DateTimeOffset.UtcNow);
        }

        SaveManifest();
    }

    private AssetCacheManifest LoadManifest()
    {
        try
        {
            if (!File.Exists(_manifestPath))
            {
                return new AssetCacheManifest();
            }

            var json = File.ReadAllText(_manifestPath);
            return JsonSerializer.Deserialize<AssetCacheManifest>(json, ManifestSerializerOptions)
                ?? new AssetCacheManifest();
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
            var tempPath = _manifestPath + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(_manifestPath))
            {
                File.Delete(_manifestPath);
            }
            File.Move(tempPath, _manifestPath);
        }
        catch
        {
            // Cache persistence is best-effort; never fail the asset load because of it.
        }
    }

    private static async Task WriteAtomicallyAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        var tempPath = path + ".tmp";
        await using (var target = File.Create(tempPath))
        {
            await content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
        File.Move(tempPath, path);
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
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

        public AssetCacheEntry(AssetKind assetKind, string sourceUrl, string pluginVersion, DateTimeOffset cachedAt)
        {
            AssetKind = assetKind;
            SourceUrl = sourceUrl;
            PluginVersion = pluginVersion;
            CachedAt = cachedAt;
        }

        [JsonPropertyName("assetKind")]
        public AssetKind AssetKind { get; init; }

        [JsonPropertyName("sourceUrl")]
        public string SourceUrl { get; init; } = string.Empty;

        [JsonPropertyName("pluginVersion")]
        public string PluginVersion { get; init; } = string.Empty;

        [JsonPropertyName("cachedAt")]
        public DateTimeOffset CachedAt { get; init; }
    }
}
