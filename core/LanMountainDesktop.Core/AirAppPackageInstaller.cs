using LanMountainDesktop.Shared.IO;
namespace LanMountainDesktop.AirAppPackaging;

public sealed class AirAppPackageInstaller
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500)
    ];

    public AirAppPackageInstallResult Install(
        string sourcePackagePath,
        string airAppsDirectory,
        AirAppPackageInstallOptions? options = null,
        Action<AirAppPackageManifest>? prepareManifest = null)
    {
        options ??= AirAppPackageInstallOptions.Default;
        var fullSourcePath = Path.GetFullPath(sourcePackagePath);
        var fullAirAppsDirectory = Path.GetFullPath(airAppsDirectory);

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"AirApp package '{fullSourcePath}' was not found.", fullSourcePath);
        }

        var manifest = AirAppPackageManifestReader.Read(fullSourcePath, options.IncludeLegacyPackages);
        prepareManifest?.Invoke(manifest);

        Directory.CreateDirectory(fullAirAppsDirectory);
        var destinationPath = Path.Combine(fullAirAppsDirectory, BuildInstalledPackageFileName(manifest.Id));
        var stagingPath = destinationPath + ".incoming";
        DeleteFileWithRetry(stagingPath);
        CopyWithRetry(fullSourcePath, stagingPath, overwrite: true);
        RemoveExistingAirAppPackages(fullAirAppsDirectory, manifest.Id, destinationPath, stagingPath, options);
        MoveWithOverwriteRetry(stagingPath, destinationPath);

        return new AirAppPackageInstallResult(destinationPath, manifest);
    }

    private static void RemoveExistingAirAppPackages(
        string airAppsDirectory,
        string airAppId,
        string destinationPath,
        string stagingPath,
        AirAppPackageInstallOptions options)
    {
        var runtimeRootDirectory = PathSeparators.EnsureTrailingSeparator(
            Path.Combine(Path.GetFullPath(airAppsDirectory), AirAppPackagingConstants.RuntimeDirectoryName));
        var pendingDeletionDir = Path.Combine(airAppsDirectory, AirAppPackagingConstants.PendingDeletionDirectoryName);
        Directory.CreateDirectory(pendingDeletionDir);

        foreach (var existingPackagePath in EnumerateExistingPackages(airAppsDirectory, options)
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

                var existingManifest = AirAppPackageManifestReader.Read(existingPackagePath, options.IncludeLegacyPackages);
                if (!string.Equals(existingManifest.Id, airAppId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TryRemoveExistingPackage(existingPackagePath, pendingDeletionDir);
            }
            catch
            {
                // Ignore unrelated or malformed packages while replacing one AirApp id.
            }
        }

        CleanupPendingDeletions(pendingDeletionDir);
    }

    private static IEnumerable<string> EnumerateExistingPackages(string airAppsDirectory, AirAppPackageInstallOptions options)
    {
        if (options.IncludeLegacyPackages)
        {
            return Directory
                .EnumerateFiles(airAppsDirectory, "*", SearchOption.AllDirectories)
                .Where(path =>
                    path.EndsWith(AirAppPackagingConstants.PackageFileExtension, StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(AirAppPackagingConstants.LegacyPackageFileExtension, StringComparison.OrdinalIgnoreCase));
        }

        return Directory.EnumerateFiles(
            airAppsDirectory,
            $"*{AirAppPackagingConstants.PackageFileExtension}",
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

    private static string BuildInstalledPackageFileName(string airAppId)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var fileName = new string(airAppId.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return fileName + AirAppPackagingConstants.PackageFileExtension;
    }

}
