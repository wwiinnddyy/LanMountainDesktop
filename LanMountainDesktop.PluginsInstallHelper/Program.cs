using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.PluginSdk;

internal static class Program
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500)
    ];

    private static async Task<int> Main(string[] args)
    {
        var result = new HelperResult();
        string? resultPath = null;

        try
        {
            var parsedArgs = ParseArgs(args);
            if (!parsedArgs.TryGetValue("source", out var sourcePath) ||
                !parsedArgs.TryGetValue("plugins-dir", out var pluginsDirectory) ||
                !parsedArgs.TryGetValue("result", out resultPath) ||
                string.IsNullOrWhiteSpace(sourcePath) ||
                string.IsNullOrWhiteSpace(pluginsDirectory) ||
                string.IsNullOrWhiteSpace(resultPath))
            {
                throw new InvalidOperationException("Required arguments: --source <path> --plugins-dir <path> --result <path>.");
            }

            var fullSourcePath = Path.GetFullPath(sourcePath);
            var fullPluginsDirectory = Path.GetFullPath(pluginsDirectory);
            resultPath = Path.GetFullPath(resultPath);

            if (!File.Exists(fullSourcePath))
            {
                throw new FileNotFoundException($"Plugin package '{fullSourcePath}' was not found.", fullSourcePath);
            }

            var manifest = ReadManifestFromPackage(fullSourcePath);
            Directory.CreateDirectory(fullPluginsDirectory);
            var destinationPath = Path.Combine(fullPluginsDirectory, BuildInstalledPackageFileName(manifest.Id));
            var stagingPath = destinationPath + ".incoming";
            DeleteFileWithRetry(stagingPath);
            CopyWithRetry(fullSourcePath, stagingPath, overwrite: true);
            RemoveExistingPluginPackages(fullPluginsDirectory, manifest.Id, destinationPath, stagingPath);
            MoveWithOverwriteRetry(stagingPath, destinationPath);

            result = new HelperResult
            {
                Success = true,
                InstalledPackagePath = destinationPath,
                ManifestId = manifest.Id,
                ManifestName = manifest.Name
            };
        }
        catch (Exception ex)
        {
            result = new HelperResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }

        if (!string.IsNullOrWhiteSpace(resultPath))
        {
            var resultDirectory = Path.GetDirectoryName(resultPath);
            if (!string.IsNullOrWhiteSpace(resultDirectory))
            {
                Directory.CreateDirectory(resultDirectory);
            }

            await File.WriteAllTextAsync(
                resultPath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true
                }),
                Encoding.UTF8);
        }

        return result.Success ? 0 : 1;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = current[2..];
            if (string.IsNullOrWhiteSpace(key) || i + 1 >= args.Length)
            {
                continue;
            }

            values[key] = args[++i];
        }

        return values;
    }

    private static PluginManifest ReadManifestFromPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.Name, PluginSdkInfo.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException(
                $"Plugin package '{packagePath}' does not contain '{PluginSdkInfo.ManifestFileName}'.");
        }

        if (entries.Length > 1)
        {
            throw new InvalidOperationException(
                $"Plugin package '{packagePath}' contains multiple '{PluginSdkInfo.ManifestFileName}' files.");
        }

        using var stream = entries[0].Open();
        return PluginManifest.Load(stream, $"{packagePath}!/{entries[0].FullName}");
    }

    private static void RemoveExistingPluginPackages(string pluginsDirectory, string pluginId, string destinationPath, string stagingPath)
    {
        var runtimeRootDirectory = EnsureTrailingSeparator(Path.Combine(Path.GetFullPath(pluginsDirectory), PluginSdkInfo.RuntimeDirectoryName));
        var pendingDeletionDir = Path.Combine(pluginsDirectory, ".pending-deletions");
        Directory.CreateDirectory(pendingDeletionDir);

        foreach (var existingPackagePath in Directory
                     .EnumerateFiles(pluginsDirectory, "*" + PluginSdkInfo.PackageFileExtension, SearchOption.AllDirectories)
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
                // Ignore unrelated or malformed packages while replacing an install target.
            }
        }

        CleanupPendingDeletions(pendingDeletionDir);
    }

    private static void TryRemoveExistingPackage(string existingPackagePath, string pendingDeletionDir)
    {
        try
        {
            DeleteFileWithRetry(existingPackagePath);
        }
        catch (IOException)
        {
            var fileName = Path.GetFileName(existingPackagePath);
            var pendingPath = Path.Combine(pendingDeletionDir, $"{fileName}.{Guid.NewGuid():N}.pending");
            try
            {
                File.Move(existingPackagePath, pendingPath);
            }
            catch (IOException moveEx)
            {
                throw new IOException(
                    $"Cannot delete or move existing plugin package '{existingPackagePath}'. " +
                    $"The file may be in use by another process. Error: {moveEx.Message}", moveEx);
            }
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
                // Ignore cleanup failures for pending deletions.
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
        return fileName + PluginSdkInfo.PackageFileExtension;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private sealed class HelperResult
    {
        public bool Success { get; init; }

        public string? InstalledPackagePath { get; init; }

        public string? ManifestId { get; init; }

        public string? ManifestName { get; init; }

        public string? ErrorMessage { get; init; }
    }
}
