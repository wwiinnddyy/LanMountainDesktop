using System.IO.Compression;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Services;

/// <summary>
/// 插件安装服务 - 简化版，不依赖 PluginSdk
/// </summary>
internal sealed class PluginInstallerService
{
    private const string ManifestFileName = "manifest.json";
    private const string PackageFileExtension = ".lmdp";
    private const string RuntimeDirectoryName = "runtime";
    
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500)
    ];

    public LauncherResult InstallPackage(string sourcePath, string pluginsDirectory)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullPluginsDirectory = Path.GetFullPath(pluginsDirectory);

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"Plugin package '{fullSourcePath}' was not found.", fullSourcePath);
        }

        if (TryBuildElevationRequiredResult(fullPluginsDirectory) is { } elevationRequiredResult)
        {
            return elevationRequiredResult;
        }

        var manifest = ReadManifestFromPackage(fullSourcePath);
        Directory.CreateDirectory(fullPluginsDirectory);
        var destinationPath = Path.Combine(fullPluginsDirectory, BuildInstalledPackageFileName(manifest.Id));
        var stagingPath = destinationPath + ".incoming";
        DeleteFileWithRetry(stagingPath);
        CopyWithRetry(fullSourcePath, stagingPath, overwrite: true);
        RemoveExistingPluginPackages(fullPluginsDirectory, manifest.Id, destinationPath, stagingPath);
        MoveWithOverwriteRetry(stagingPath, destinationPath);

        return new LauncherResult
        {
            Success = true,
            Stage = "plugin.install",
            Code = "ok",
            Message = "Plugin installed.",
            InstalledPackagePath = destinationPath,
            ManifestId = manifest.Id,
            ManifestName = manifest.Name
        };
    }

    private static LauncherResult? TryBuildElevationRequiredResult(string pluginsDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return null;
        }

        var allowedRoot = EnsureTrailingSeparator(Path.Combine(Path.GetFullPath(localAppData), "LanMountainDesktop"));
        var normalizedPluginsDirectory = EnsureTrailingSeparator(Path.GetFullPath(pluginsDirectory));
        if (normalizedPluginsDirectory.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Logger.Warn(
            $"Plugin installation requires explicit elevation. Reason='plugin_requires_elevation'; " +
            $"PluginsDirectory='{pluginsDirectory}'; AllowedRoot='{allowedRoot}'.");

        return new LauncherResult
        {
            Success = false,
            Stage = "plugin.install",
            Code = "plugin_elevation_required",
            Message = "Plugin installation outside the current user's LanMountainDesktop data directory requires explicit elevation.",
            ErrorMessage = "Plugin installation target is outside the current user's LanMountainDesktop data directory.",
            Details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pluginsDirectory"] = pluginsDirectory,
                ["allowedRoot"] = allowedRoot,
                ["elevationReason"] = "outside_user_scope"
            }
        };
    }

    public PluginManifest ReadManifestFromPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.Name, ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException(
                $"Plugin package '{packagePath}' does not contain '{ManifestFileName}'.");
        }

        if (entries.Length > 1)
        {
            throw new InvalidOperationException(
                $"Plugin package '{packagePath}' contains multiple '{ManifestFileName}' files.");
        }

        using var stream = entries[0].Open();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var manifest = JsonSerializer.Deserialize(json, AppJsonContext.Default.PluginManifest);
        if (manifest == null)
        {
            throw new InvalidOperationException($"Failed to deserialize manifest from '{packagePath}'.");
        }
        return manifest;
    }

    private void RemoveExistingPluginPackages(string pluginsDirectory, string pluginId, string destinationPath, string stagingPath)
    {
        var runtimeRootDirectory = EnsureTrailingSeparator(Path.Combine(Path.GetFullPath(pluginsDirectory), RuntimeDirectoryName));
        var pendingDeletionDir = Path.Combine(pluginsDirectory, ".pending-deletions");
        Directory.CreateDirectory(pendingDeletionDir);

        foreach (var existingPackagePath in Directory
                     .EnumerateFiles(pluginsDirectory, "*" + PackageFileExtension, SearchOption.AllDirectories)
                     .Select(Path.GetFullPath)
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
public class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Description { get; set; }
    public string? Author { get; set; }
}
