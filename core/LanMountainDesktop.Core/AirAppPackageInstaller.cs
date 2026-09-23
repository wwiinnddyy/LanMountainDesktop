using LanMountainDesktop.Shared.IO;
namespace LanMountainDesktop.AirAppPackaging;

public sealed class AirAppPackageInstaller
{
    private const string RetryCategory = "AirAppPackageInstaller";

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
        FileOperationRetryHelper.DeleteFileWithRetry(stagingPath, RetryCategory);
        FileOperationRetryHelper.CopyWithRetry(fullSourcePath, stagingPath, overwrite: true, RetryCategory);
        RemoveExistingAirAppPackages(fullAirAppsDirectory, manifest.Id, destinationPath, stagingPath, options);
        FileOperationRetryHelper.MoveWithOverwriteRetry(stagingPath, destinationPath, RetryCategory);

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
        var pendingDeletionDir = AirAppPendingDeletionDirectory.PathFor(airAppsDirectory);
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

                AirAppPendingDeletionDirectory.RemoveOrMoveToPending(existingPackagePath, pendingDeletionDir, RetryCategory);
            }
            catch
            {
                // Ignore unrelated or malformed packages while replacing one AirApp id.
            }
        }

        AirAppPendingDeletionDirectory.CleanupAfterInstall(pendingDeletionDir);
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

    private static string BuildInstalledPackageFileName(string airAppId)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var fileName = new string(airAppId.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return fileName + AirAppPackagingConstants.PackageFileExtension;
    }

}
