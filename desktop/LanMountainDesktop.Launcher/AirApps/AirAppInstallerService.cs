using System.IO.Compression;
using LanMountainDesktop.Shared.Data;
using System.Text.Json;
using LanMountainDesktop.AirAppPackaging;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Launcher.AirApps;

/// <summary>
/// AirApp 安装服务 - 简化版，不依赖 AirAppSdk
/// </summary>
internal sealed class AirAppInstallerService
{
    private const string PackageFileExtension = AirAppPackagingConstants.PackageFileExtension;
    private const string LegacyPackageFileExtension = AirAppPackagingConstants.LegacyPackageFileExtension;

    // 宿主的运行时目录是 AirAppPackagingConstants.RuntimeDirectoryName（".runtime"）。这里故意不取该值：
    // 当前字面量使 RemoveExistingAirAppPackages 的排除条件不生效，从而连带删掉本应用在 .runtime 下的旧包副本。
    // 对齐成 ".runtime" 会让旧副本存活，而 AirAppLoader 是递归扫描候选包的，可能出现重复加载。改动需单独决策。
    private const string RuntimeDirectoryName = "runtime";
    
    private const string RetryCategory = "AirAppInstaller";

    public LauncherResult InstallPackage(string sourcePath, string airAppsDirectory, string? appRoot = null)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullAirAppsDirectory = Path.GetFullPath(airAppsDirectory);

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"AirApp package '{fullSourcePath}' was not found.", fullSourcePath);
        }

        if (TryBuildElevationRequiredResult(fullAirAppsDirectory, appRoot) is { } elevationRequiredResult)
        {
            return elevationRequiredResult;
        }

        var manifest = AirAppPackageManifestReader.Read(fullSourcePath, includeLegacyManifest: true);
        Directory.CreateDirectory(fullAirAppsDirectory);
        var destinationPath = Path.Combine(fullAirAppsDirectory, BuildInstalledPackageFileName(manifest.Id));
        var stagingPath = destinationPath + ".incoming";
        FileOperationRetryHelper.DeleteFileWithRetry(stagingPath, RetryCategory);
        FileOperationRetryHelper.CopyWithRetry(fullSourcePath, stagingPath, overwrite: true, RetryCategory);
        RemoveExistingAirAppPackages(fullAirAppsDirectory, manifest.Id, destinationPath, stagingPath);
        FileOperationRetryHelper.MoveWithOverwriteRetry(stagingPath, destinationPath, RetryCategory);

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

    private static LauncherResult? TryBuildElevationRequiredResult(string airAppsDirectory, string? appRoot)
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
            allowedRoot = PathSeparators.EnsureTrailingSeparator(resolver.ResolveDataRoot());
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

            allowedRoot = PathSeparators.EnsureTrailingSeparator(Path.Combine(Path.GetFullPath(localAppData), UserDataRoot.FolderName));
        }

        var normalizedAirAppsDirectory = PathSeparators.EnsureTrailingSeparator(Path.GetFullPath(airAppsDirectory));
        if (normalizedAirAppsDirectory.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Logger.Warn(
            $"AirApp installation requires explicit elevation. Reason='plugin_requires_elevation'; " +
            $"AirAppsDirectory='{airAppsDirectory}'; AllowedRoot='{allowedRoot}'.");

        return new LauncherResult
        {
            Success = false,
            Stage = "plugin.install",
            Code = "plugin_elevation_required",
            Message = "AirApp installation outside the current user's LanMountainDesktop data directory requires explicit elevation.",
            ErrorMessage = "AirApp installation target is outside the current user's LanMountainDesktop data directory.",
            Details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["airAppsDirectory"] = airAppsDirectory,
                ["allowedRoot"] = allowedRoot,
                ["elevationReason"] = "outside_user_scope"
            }
        };
    }

    private void RemoveExistingAirAppPackages(string airAppsDirectory, string airAppId, string destinationPath, string stagingPath)
    {
        var runtimeRootDirectory = PathSeparators.EnsureTrailingSeparator(Path.Combine(Path.GetFullPath(airAppsDirectory), RuntimeDirectoryName));
        var pendingDeletionDir = AirAppPendingDeletionDirectory.PathFor(airAppsDirectory);
        Directory.CreateDirectory(pendingDeletionDir);

        foreach (var existingPackagePath in Directory
                     .EnumerateFiles(airAppsDirectory, "*", SearchOption.AllDirectories)
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

                var existingManifest = AirAppPackageManifestReader.Read(existingPackagePath, includeLegacyManifest: true);
                if (!string.Equals(existingManifest.Id, airAppId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AirAppPendingDeletionDirectory.RemoveOrMoveToPending(existingPackagePath, pendingDeletionDir, RetryCategory);
            }
            catch
            {
            }
        }

        AirAppPendingDeletionDirectory.CleanupAfterInstall(pendingDeletionDir);
    }

    private static string BuildInstalledPackageFileName(string airAppId)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var fileName = new string(airAppId.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return fileName + PackageFileExtension;
    }

}
