using System.IO.Compression;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.AirApps;

/// <summary>
/// 插件安装服务 - 简化版，不依赖 AirAppSdk
/// </summary>
internal sealed class AirAppInstallerService
{
    private const string ManifestFileName = "airapp.json";
    private const string LegacyManifestFileName = "manifest.json";
    private const string PackageFileExtension = ".laapp";
    private const string LegacyPackageFileExtension = ".lmdp";
    private const string RuntimeDirectoryName = "runtime";
    
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500)
    ];

    public LauncherResult InstallPackage(string sourcePath, string pluginsDirectory, string? appRoot = null)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullAirAppsDirectory = Path.GetFullPath(pluginsDirectory);

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"AirApp package '{fullSourcePath}' was not found.", fullSourcePath);
        }

        if (TryBuildElevationRequiredResult(fullAirAppsDirectory, appRoot) is { } elevationRequiredResult)
        {
            return elevationRequiredResult;
        }

        var manifest = ReadManifestFromPackage(fullSourcePath);
        Directory.CreateDirectory(fullAirAppsDirectory);
        var destinationPath = Path.Combine(fullAirAppsDirectory, BuildInstalledPackageFileName(manifest.Id));
        var stagingPath = destinationPath + ".incoming";
        DeleteFileWithRetry(stagingPath);
        CopyWithRetry(fullSourcePath, stagingPath, overwrite: true);
        RemoveExistingAirAppPackages(fullAirAppsDirectory, manifest.Id, destinationPath, stagingPath);
        MoveWithOverwriteRetry(stagingPath, destinationPath);

        return new LauncherResult
        {
            Success = true,
            Stage = "plugin.install",
            Code = "ok",
            Message = "AirApp installed.",
            InstalledPackagePath = destinationPath,
            ManifestId = manifest.Id,
            ManifestName = manifest.Name
        };
    }

    private static LauncherResult? TryBuildElevationRequiredResult(string pluginsDirectory, string? appRoot)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string? allowedRoot = null;
        try
        {
            var resolvedAppRoot = !string.IsNullOrWhiteSpace(appRoot)
                ? Path.GetFullPath(appRoot)
                : Commands.ResolveAppRoot(CommandContext.FromArgs([]));
            var resolver = new DataLocationResolver(resolvedAppRoot);
            allowedRoot = EnsureTrailingSeparator(resolver.ResolveDataRoot());
        }
        catch
        {
        }

        if (string.IsNullOrWhiteSpace(allowedRoot))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return null;
            }

            allowedRoot = EnsureTrailingSeparator(Path.Combine(Path.GetFullPath(localAppData), "LanMountainDesktop"));
        }

        var normalizedAirAppsDirectory = EnsureTrailingSeparator(Path.GetFullPath(pluginsDirectory));
        if (normalizedAirAppsDirectory.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Logger.Warn(
            $"AirApp installation requires explicit elevation. Reason='plugin_requires_elevation'; " +
            $"AirAppsDirectory='{pluginsDirectory}'; AllowedRoot='{allowedRoot}'.");

        return new LauncherResult
        {
            Success = false,
            Stage = "plugin.install",
            Code = "plugin_elevation_required",
            Message = "AirApp installation outside the current user's LanMountainDesktop data directory requires explicit elevation.",
            ErrorMessage = "AirApp installation target is outside the current user's LanMountainDesktop data directory.",
            Details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pluginsDirectory"] = pluginsDirectory,
                ["allowedRoot"] = allowedRoot,
                ["elevationReason"] = "outside_user_scope"
            }
        };
    }

    public AirAppManifest ReadManifestFromPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = FindManifestEntries(archive, ManifestFileName);
        if (entries.Length == 0)
        {
            entries = FindManifestEntries(archive, LegacyManifestFileName);
        }

        if (entries.Length == 0)
        {
            throw new InvalidOperationException(
                $"AirApp package '{packagePath}' does not contain '{ManifestFileName}' or '{LegacyManifestFileName}'.");
        }

        if (entries.Length > 1)
        {
            throw new InvalidOperationException(
                $"AirApp package '{packagePath}' contains multiple '{ManifestFileName}' files.");
        }

        using var stream = entries[0].Open();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var manifest = JsonSerializer.Deserialize(json, AppJsonContext.Default.AirAppManifest);
        if (manifest == null)
        {
            throw new InvalidOperationException($"Failed to deserialize manifest from '{packagePath}'.");
        }
        return manifest;
    }

    private static ZipArchiveEntry[] FindManifestEntries(ZipArchive archive, string manifestFileName)
    {
        return archive.Entries
            .Where(entry => string.Equals(entry.Name, manifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private void RemoveExistingAirAppPackages(string pluginsDirectory, string pluginId, string destinationPath, string stagingPath)
    {
        var runtimeRootDirectory = EnsureTrailingSeparator(Path.Combine(Path.GetFullPath(pluginsDirectory), RuntimeDirectoryName));
        var pendingDeletionDir = Path.Combine(pluginsDirectory, ".pending-deletions");
        Directory.CreateDirectory(pendingDeletionDir);

        foreach (var existingPackagePath in Directory
                     .EnumerateFiles(pluginsDirectory, "*", SearchOption.AllDirectories)
                     .Select(Path.GetFullPath)
                     .Where(path =>
                         path.EndsWith(PackageFileExtension, StringComparison.OrdinalIgnoreCase) ||
                         path.EndsWith(LegacyPackageFileExtension, StringComparison.OrdinalIgnoreCase))
                     .Where(path => !path.StartsWith(runtimeRootDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (string.Equals(existingPackagePath, Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(existingPackagePath, Path.GetFullPath(stagingPath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var existingManifest = ReadManifestFromPackage(existingPackagePath);
                if (!string.Equals(existingManifest.Id, pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TryRemoveExistingPackage(existingPackagePath, pendingDeletionDir);
            }
            catch
            {
            }
        }

        CleanupPendingDeletions(pendingDeletionDir);
    }

    private void TryRemoveExistingPackage(string existingPackagePath, string pendingDeletionDir)
    {
        try
        {
            DeleteFileWithRetry(existingPackagePath);
        }
        catch (IOException)
        {
            var fileName = Path.GetFileName(existingPackagePath);
            var pendingPath = Path.Combine(pendingDeletionDir, $"{fileName}.{Guid.NewGuid():N}.pending");
            File.Move(existingPackagePath, pendingPath);
        }
    }

    private static void CleanupPendingDeletions(string pendingDeletionDir)
    {
        if (!Directory.Exists(pendingDeletionDir))
        {
            return;
        }

        foreach (var pendingFile in Directory.EnumerateFiles(pendingDeletionDir, "*.pending"))
        {
            try
            {
                File.Delete(pendingFile);
            }
            catch
            {
            }
        }
    }

    private static void CopyWithRetry(string sourcePath, string destinationPath, bool overwrite)
    {
        Retry(() => File.Copy(sourcePath, destinationPath, overwrite));
    }

    private static void MoveWithOverwriteRetry(string sourcePath, string destinationPath)
    {
        Retry(() => File.Move(sourcePath, destinationPath, overwrite: true));
    }

    private static void DeleteFileWithRetry(string filePath)
    {
        Retry(() =>
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        });
    }

    private static void Retry(Action action)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastException = ex;
                if (attempt >= RetryDelays.Length)
                {
                    break;
                }

                Thread.Sleep(RetryDelays[attempt]);
            }
        }

        if (lastException is not null)
        {
            throw lastException;
        }
    }

    private static string BuildInstalledPackageFileName(string pluginId)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var fileName = new string(pluginId.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return fileName + PackageFileExtension;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}

/// <summary>
/// 简化的插件清单模型
/// </summary>
internal class AirAppManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Description { get; set; }
    public string? Author { get; set; }
}
