using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

internal readonly record struct WallpaperSeedSourceDescriptor(
    string SourceKind,
    string SourceKey,
    string? ResolvedWallpaperPath,
    string? FilePath,
    Color? SolidColor);

internal sealed record WallpaperSeedExtractionResult(
    string SourceKind,
    string SourceKey,
    string? ResolvedWallpaperPath,
    IReadOnlyList<Color> SeedCandidates);

internal readonly record struct WallpaperPaletteResolution(
    MonetPalette Palette,
    IReadOnlyList<Color> SeedCandidates,
    string ResolvedSeedSource,
    Color EffectiveSeedColor,
    string? ResolvedWallpaperPath);

internal sealed class WallpaperColorPipeline
{
    private static readonly Color NeutralFallbackSeedColor = Color.Parse("#FF8A8A8A");

    private readonly ISettingsFacadeService _settingsFacade;
    private readonly ISystemWallpaperProvider _systemWallpaperProvider;
    private readonly MonetColorService _monetColorService;
    private readonly Action<bool> _notifyChanged;
    private readonly object _gate = new();
    private readonly Dictionary<string, WallpaperSeedExtractionResult> _seedCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingSeedKeys = new(StringComparer.OrdinalIgnoreCase);

    public WallpaperColorPipeline(
        ISettingsFacadeService settingsFacade,
        ISystemWallpaperProvider systemWallpaperProvider,
        MonetColorService monetColorService,
        Action<bool> notifyChanged)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _systemWallpaperProvider = systemWallpaperProvider ?? throw new ArgumentNullException(nameof(systemWallpaperProvider));
        _monetColorService = monetColorService ?? throw new ArgumentNullException(nameof(monetColorService));
        _notifyChanged = notifyChanged ?? throw new ArgumentNullException(nameof(notifyChanged));
    }

    public void Clear()
    {
        lock (_gate)
        {
            _seedCache.Clear();
            _pendingSeedKeys.Clear();
        }
    }

    public WallpaperPaletteResolution Resolve(
        bool nightMode,
        WallpaperSettingsState wallpaperState,
        string wallpaperColorSource,
        string? selectedWallpaperSeed,
        bool queueWallpaperPaletteBuild)
    {
        var source = ResolveSource(wallpaperState, wallpaperColorSource);
        if (string.Equals(source.SourceKind, "fallback", StringComparison.OrdinalIgnoreCase))
        {
            return BuildFallbackResolution(nightMode, source.ResolvedWallpaperPath);
        }

        if (string.Equals(source.SourceKind, "app_solid", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = source.SolidColor is { } solidColor
                ? new[] { solidColor }
                : [];
            return BuildResolution(nightMode, source, candidates, selectedWallpaperSeed);
        }

        lock (_gate)
        {
            if (_seedCache.TryGetValue(source.SourceKey, out var cachedSeedResult))
            {
                if (cachedSeedResult.SeedCandidates.Count > 0)
                {
                    return BuildResolution(
                        nightMode,
                        source with
                        {
                            SourceKind = cachedSeedResult.SourceKind,
                            ResolvedWallpaperPath = cachedSeedResult.ResolvedWallpaperPath
                        },
                        cachedSeedResult.SeedCandidates,
                        selectedWallpaperSeed);
                }

                return BuildFallbackResolution(nightMode, cachedSeedResult.ResolvedWallpaperPath);
            }
        }

        if (queueWallpaperPaletteBuild)
        {
            QueueSeedExtraction(source);
        }

        return BuildFallbackResolution(nightMode, source.ResolvedWallpaperPath);
    }

    public WallpaperSeedSourceDescriptor ResolveSource(
        WallpaperSettingsState wallpaperState,
        string wallpaperColorSource)
    {
        var normalizedWallpaperColorSource = ThemeAppearanceValues.NormalizeWallpaperColorSource(wallpaperColorSource);

        if (normalizedWallpaperColorSource != ThemeAppearanceValues.WallpaperColorSourceSystem &&
            string.Equals(wallpaperState.Type, "SolidColor", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(wallpaperState.Color) &&
            Color.TryParse(wallpaperState.Color, out var solidColor))
        {
            var solidText = solidColor.ToString();
            return new WallpaperSeedSourceDescriptor(
                "app_solid",
                $"app_solid|{solidText}",
                null,
                null,
                solidColor);
        }

        var wallpaperPath = string.IsNullOrWhiteSpace(wallpaperState.WallpaperPath)
            ? null
            : wallpaperState.WallpaperPath.Trim();
        var appWallpaperMediaType = _settingsFacade.WallpaperMedia.DetectMediaType(wallpaperPath);
        if (normalizedWallpaperColorSource != ThemeAppearanceValues.WallpaperColorSourceSystem &&
            !string.IsNullOrWhiteSpace(wallpaperPath) &&
            File.Exists(wallpaperPath) &&
            appWallpaperMediaType == WallpaperMediaType.Image)
        {
            return new WallpaperSeedSourceDescriptor(
                "app_wallpaper",
                CreateWallpaperSourceKey("app_wallpaper", wallpaperPath),
                wallpaperPath,
                wallpaperPath,
                null);
        }

        if (normalizedWallpaperColorSource == ThemeAppearanceValues.WallpaperColorSourceApp)
        {
            return new WallpaperSeedSourceDescriptor(
                "fallback",
                "fallback",
                null,
                null,
                null);
        }

        var systemWallpaper = _systemWallpaperProvider.GetWallpaperPath();
        if (normalizedWallpaperColorSource != ThemeAppearanceValues.WallpaperColorSourceApp &&
            !string.IsNullOrWhiteSpace(systemWallpaper) &&
            File.Exists(systemWallpaper) &&
            _settingsFacade.WallpaperMedia.DetectMediaType(systemWallpaper) == WallpaperMediaType.Image)
        {
            return new WallpaperSeedSourceDescriptor(
                "system_wallpaper",
                CreateWallpaperSourceKey("system_wallpaper", systemWallpaper),
                systemWallpaper,
                systemWallpaper,
                null);
        }

        return new WallpaperSeedSourceDescriptor(
            "fallback",
            "fallback",
            null,
            null,
            null);
    }

    private void QueueSeedExtraction(WallpaperSeedSourceDescriptor source)
    {
        if (string.Equals(source.SourceKind, "fallback", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source.SourceKind, "app_solid", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_gate)
        {
            if (_pendingSeedKeys.Contains(source.SourceKey))
            {
                return;
            }

            _pendingSeedKeys.Add(source.SourceKey);
        }

        _ = Task.Run(() =>
        {
            WallpaperSeedExtractionResult? extractionResult = null;

            try
            {
                extractionResult = ExtractSeedCandidates(source);
            }
            catch (Exception ex)
            {
                AppLogger.Warn(
                    "Appearance.WallpaperSeed",
                    $"Failed to build wallpaper seed candidates asynchronously. Source='{source.SourceKind}'; Path='{source.FilePath}'.",
                    ex);
            }
            finally
            {
                lock (_gate)
                {
                    _pendingSeedKeys.Remove(source.SourceKey);
                    if (extractionResult is not null)
                    {
                        _seedCache[source.SourceKey] = extractionResult;
                    }
                }
            }

            if (extractionResult is not null)
            {
                _notifyChanged(false);
            }
        });
    }

    private WallpaperSeedExtractionResult ExtractSeedCandidates(WallpaperSeedSourceDescriptor source)
    {
        IReadOnlyList<Color> seedCandidates = source.SourceKind switch
        {
            "app_wallpaper" or "system_wallpaper" => ExtractImageSeedCandidates(source.FilePath),
            "app_solid" when source.SolidColor is { } solidColor => new[] { solidColor },
            _ => []
        };

        return new WallpaperSeedExtractionResult(
            source.SourceKind,
            source.SourceKey,
            source.ResolvedWallpaperPath,
            seedCandidates);
    }

    private IReadOnlyList<Color> ExtractImageSeedCandidates(string? wallpaperPath)
    {
        if (string.IsNullOrWhiteSpace(wallpaperPath) || !File.Exists(wallpaperPath))
        {
            return [];
        }

        try
        {
            using var bitmap = new Bitmap(wallpaperPath);
            return _monetColorService.ExtractSeedCandidates(bitmap);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "Appearance.WallpaperSeed",
                $"Failed to extract wallpaper seed candidates from image '{wallpaperPath}'.",
                ex);
            return [];
        }
    }

    private WallpaperPaletteResolution BuildResolution(
        bool nightMode,
        WallpaperSeedSourceDescriptor source,
        IReadOnlyList<Color> seedCandidates,
        string? selectedWallpaperSeed)
    {
        var validatedSeed = ResolveSelectedWallpaperSeed(seedCandidates, selectedWallpaperSeed);
        var palette = _monetColorService.BuildPaletteFromSeedCandidates(seedCandidates, nightMode, validatedSeed);
        return new WallpaperPaletteResolution(
            palette,
            seedCandidates,
            source.SourceKind,
            palette.Seed,
            source.ResolvedWallpaperPath);
    }

    private WallpaperPaletteResolution BuildFallbackResolution(bool nightMode, string? resolvedWallpaperPath)
    {
        var palette = _monetColorService.BuildPaletteFromSeedCandidates([], nightMode, NeutralFallbackSeedColor);
        return new WallpaperPaletteResolution(
            palette,
            [],
            "fallback",
            palette.Seed,
            resolvedWallpaperPath);
    }

    private static Color? ResolveSelectedWallpaperSeed(
        IReadOnlyList<Color> seedCandidates,
        string? selectedWallpaperSeed)
    {
        if (seedCandidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(selectedWallpaperSeed) &&
            Color.TryParse(selectedWallpaperSeed, out var parsedSeed))
        {
            foreach (var candidate in seedCandidates)
            {
                if (candidate == parsedSeed)
                {
                    return candidate;
                }
            }
        }

        return seedCandidates[0];
    }

    private static string CreateWallpaperSourceKey(string sourceKind, string wallpaperPath)
    {
        long lastWriteTicks = 0;
        long length = 0;

        try
        {
            var fileInfo = new FileInfo(wallpaperPath);
            if (fileInfo.Exists)
            {
                lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;
                length = fileInfo.Length;
            }
        }
        catch
        {
            // Keep the cache key resilient even if metadata lookup fails.
        }

        return string.Concat(
            sourceKind,
            "|",
            wallpaperPath,
            "|",
            lastWriteTicks.ToString(),
            "|",
            length.ToString());
    }
}
