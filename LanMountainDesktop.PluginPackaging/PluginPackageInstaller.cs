namespace LanMountainDesktop.PluginPackaging;

public sealed class PluginPackageInstaller
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500)
    ];

    public PluginPackageInstallResult Install(
        string sourcePackagePath,
        string pluginsDirectory,
        PluginPackageInstallOptions? options = null,
        Action<PluginPackageManifest>? prepareManifest = null)
    {
        options ??= PluginPackageInstallOptions.Default;
        var fullSourcePath = Path.GetFullPath(sourcePackagePath);
        var fullPluginsDirectory = Path.GetFullPath(pluginsDirectory);

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"Plugin package '{fullSourcePath}' was not found.", fullSourcePath);
        }

        var manifest = PluginPackageManifestReader.Read(fullSourcePath, options.IncludeLegacyPackages);
        prepareManifest?.Invoke(manifest);

        Directory.CreateDirectory(fullPluginsDirectory);
        var destinationPath = Path.Combine(fullPluginsDirectory, BuildInstalledPackageFileName(manifest.Id));
        var stagingPath = destinationPath + ".incoming";
        DeleteFileWithRetry(stagingPath);
        CopyWithRetry(fullSourcePath, stagingPath, overwrite: true);
        RemoveExistingPluginPackages(fullPluginsDirectory, manifest.Id, destinationPath, stagingPath, options);
        MoveWithOverwriteRetry(stagingPath, destinationPath);

        return new PluginPackageInstallResult(destinationPath, manifest);
    }

    private static void RemoveExistingPluginPackages(
        string pluginsDirectory,
        string pluginId,
        string destinationPath,
        string stagingPath,
        PluginPackageInstallOptions options)
    {
        var runtimeRootDirectory = EnsureTrailingSeparator(
            Path.Combine(Path.GetFullPath(pluginsDirectory), PluginPackagingConstants.RuntimeDirectoryName));
        var pendingDeletionDir = Path.Combine(pluginsDirectory, PluginPackagingConstants.PendingDeletionDirectoryName);
        Directory.CreateDirectory(pendingDeletionDir);

        foreach (var existingPackagePath in EnumerateExistingPackages(pluginsDirectory, options)
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

                var existingManifest = PluginPackageManifestReader.Read(existingPackagePath, options.IncludeLegacyPackages);
                if (!string.Equals(existingManifest.Id, pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TryRemoveExistingPackage(existingPackagePath, pendingDeletionDir);
            }
            catch
            {
                // Ignore unrelated or malformed packages while replacing one plugin id.
            }
        }

        CleanupPendingDeletions(pendingDeletionDir);
    }

    private static IEnumerable<string> EnumerateExistingPackages(string pluginsDirectory, PluginPackageInstallOptions options)
    {
        if (options.IncludeLegacyPackages)
        {
            return Directory
                .EnumerateFiles(pluginsDirectory, "*", SearchOption.AllDirectories)
                .Where(path =>
                    path.EndsWith(PluginPackagingConstants.PackageFileExtension, StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(PluginPackagingConstants.LegacyPackageFileExtension, StringComparison.OrdinalIgnoreCase));
        }

        return Directory.EnumerateFiles(
            pluginsDirectory,
            $"*{PluginPackagingConstants.PackageFileExtension}",
            SearchOption.AllDirectories);
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
                // Best-effort cleanup only.
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
        return fileName + PluginPackagingConstants.PackageFileExtension;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
