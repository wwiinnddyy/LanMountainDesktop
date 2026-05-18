using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

public sealed record StorageCategoryInfo(
    string Id,
    string Name,
    string Description,
    string DirectoryPath,
    bool IsCleanable,
    string ColorHex);

public sealed record StorageScanResult(
    StorageCategoryInfo Category,
    long SizeBytes,
    double PercentageOfTotal);

public sealed class DataStorageService
{
    private static readonly string[] SettingsRootFiles =
    [
        "settings.json",
        "plugin-settings.json",
        "launcher-settings.json",
        "app.db",
        "app.db-shm",
        "app.db-wal"
    ];

    private static readonly string[] CategoryRelativeDirectories =
    [
        "Whiteboards",
        "Extensions",
        "PluginMarket",
        "Wallpapers"
    ];

    private static readonly IReadOnlyList<StorageCategoryInfo> Categories = new List<StorageCategoryInfo>
    {
        new("logs", "日志文件", "应用运行日志", "", true, "#9E9E9E"),
        new("whiteboards", "白板笔记", "桌面白板笔记数据", "Whiteboards", true, "#FF9800"),
        new("plugins", "插件数据", "已安装插件文件", "Extensions/Plugins", true, "#2196F3"),
        new("market", "插件市场缓存", "插件市场元数据缓存", "PluginMarket", true, "#9C27B0"),
        new("wallpapers", "壁纸文件", "下载的壁纸资源", "Wallpapers", true, "#E91E63"),
        new("settings", "设置文件", "应用配置数据", "", false, "#4CAF50")
    };

    public IReadOnlyList<StorageCategoryInfo> GetCategories() => Categories;

    public async Task<IReadOnlyList<StorageScanResult>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<StorageScanResult>();
        var dataRoot = AppDataPathProvider.GetDataRoot();
        var logDirectory = AppLogger.LogDirectory;

        long totalSize = 0;
        var categorySizes = new Dictionary<string, long>();

        foreach (var category in Categories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = category.Id switch
            {
                "logs" => logDirectory,
                "settings" => dataRoot,
                _ => Path.Combine(dataRoot, category.DirectoryPath)
            };

            long size = 0;
            if (category.Id == "settings")
            {
                size = await GetSettingsSizeAsync(dataRoot, cancellationToken);
            }
            else if (Directory.Exists(path))
            {
                size = await GetDirectorySizeAsync(path, cancellationToken);
            }

            categorySizes[category.Id] = size;
            totalSize += size;
        }

        foreach (var category in Categories)
        {
            var size = categorySizes.GetValueOrDefault(category.Id, 0);
            var percentage = totalSize > 0 ? (double)size / totalSize * 100 : 0;
            results.Add(new StorageScanResult(category, size, percentage));
        }

        return results;
    }

    public async Task<long> GetTotalDiskSpaceAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var dataRoot = AppDataPathProvider.GetDataRoot();
            var pathRoot = Path.GetPathRoot(dataRoot);
            if (string.IsNullOrWhiteSpace(pathRoot))
            {
                return 0;
            }

            var driveInfo = new DriveInfo(pathRoot);
            return driveInfo.TotalSize;
        }, cancellationToken);
    }

    public async Task<long> GetAvailableDiskSpaceAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var dataRoot = AppDataPathProvider.GetDataRoot();
            var pathRoot = Path.GetPathRoot(dataRoot);
            if (string.IsNullOrWhiteSpace(pathRoot))
            {
                return 0;
            }

            var driveInfo = new DriveInfo(pathRoot);
            return driveInfo.AvailableFreeSpace;
        }, cancellationToken);
    }

    public async Task<bool> CleanCategoryAsync(string categoryId, CancellationToken cancellationToken = default)
    {
        var category = Categories.FirstOrDefault(c =>
            string.Equals(c.Id, categoryId, StringComparison.OrdinalIgnoreCase));

        if (category is null || !category.IsCleanable)
        {
            return false;
        }

        var dataRoot = AppDataPathProvider.GetDataRoot();
        string path = categoryId switch
        {
            "logs" => AppLogger.LogDirectory,
            _ => Path.Combine(dataRoot, category.DirectoryPath)
        };

        if (!Directory.Exists(path))
        {
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                if (categoryId == "logs")
                {
                    foreach (var file in Directory.GetFiles(path, "*.log"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryDeleteFile(file);
                    }
                }
                else
                {
                    foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryDeleteFile(file);
                    }

                    foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryDeleteDirectory(dir);
                    }
                }

                AppLogger.Info("DataStorage", $"Cleaned category '{categoryId}' at '{path}'.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DataStorage", $"Failed to clean category '{categoryId}'.", ex);
                return false;
            }
        }, cancellationToken);
    }

    private static async Task<long> GetDirectorySizeAsync(string path, CancellationToken cancellationToken)
    {
        return await Task.Run(() => GetDirectorySizeCore(path, cancellationToken), cancellationToken);
    }

    private static async Task<long> GetSettingsSizeAsync(string dataRoot, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            long size = 0;
            var countedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in SettingsRootFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(dataRoot, file);
                if (File.Exists(path))
                {
                    try
                    {
                        var fullPath = Path.GetFullPath(path);
                        size += new FileInfo(fullPath).Length;
                        countedFiles.Add(fullPath);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }

            // Include root-level auxiliary files (exclude known category directories and logs).
            try
            {
                var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var relativeDir in CategoryRelativeDirectories)
                {
                    excludedDirs.Add(Path.GetFullPath(Path.Combine(dataRoot, relativeDir)));
                }

                var logDir = AppLogger.LogDirectory;
                if (!string.IsNullOrWhiteSpace(logDir))
                {
                    excludedDirs.Add(Path.GetFullPath(logDir));
                }

                foreach (var file in Directory.EnumerateFiles(dataRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists)
                        {
                            continue;
                        }

                        var fullPath = Path.GetFullPath(file);
                        if (countedFiles.Contains(fullPath))
                        {
                            continue;
                        }

                        size += info.Length;
                        countedFiles.Add(fullPath);
                    }
                    catch
                    {
                        // Ignore file probe failures
                    }
                }

                foreach (var directory in Directory.EnumerateDirectories(dataRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fullPath = Path.GetFullPath(directory);
                    if (excludedDirs.Contains(fullPath))
                    {
                        continue;
                    }

                    size += GetDirectorySizeCore(fullPath, cancellationToken);
                }
            }
            catch
            {
                // Ignore directory enumeration errors
            }

            return size;
        }, cancellationToken);
    }

    private static long GetDirectorySizeCore(string path, CancellationToken cancellationToken)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    if (info.Exists)
                    {
                        size += info.Length;
                    }
                }
                catch
                {
                    // Ignore files we can't access
                }
            }
        }
        catch
        {
            // Ignore directories we can't access
        }

        return size;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch
        {
            // Ignore deletion failures
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, false);
        }
        catch
        {
            // Ignore deletion failures
        }
    }

    public static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;
        const long TB = GB * 1024;

        return bytes switch
        {
            >= TB => $"{bytes / (double)TB:F2} TB",
            >= GB => $"{bytes / (double)GB:F2} GB",
            >= MB => $"{bytes / (double)MB:F2} MB",
            >= KB => $"{bytes / (double)KB:F2} KB",
            _ => $"{bytes} B"
        };
    }
}
