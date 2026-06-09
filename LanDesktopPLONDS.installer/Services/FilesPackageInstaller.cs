using System.Diagnostics;
using LanDesktopPLONDS.Installer.Models;

namespace LanDesktopPLONDS.Installer.Services;

internal sealed class FilesPackageInstaller
{
    public async Task InstallAsync(
        PreparedFilesPackage package,
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        await InstallAsync(package, installPath, OnlineInstallOptions.Default, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task InstallAsync(
        PreparedFilesPackage package,
        string installPath,
        OnlineInstallOptions options,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        var launcherRoot = InstallerPathGuard.NormalizeInstallPath(installPath);
        var sourceAppDirectory = ResolveFullPackageAppDirectory(package.ExtractDirectory, package.Version);
        var targetDeployment = BuildDeploymentDirectory(launcherRoot, package.Version);

        InstallerElevation.EnsureCanInstall(launcherRoot);
        InstallerPathGuard.EnsureUsableInstallPath(launcherRoot, EstimateRequiredBytes(sourceAppDirectory));
        Directory.CreateDirectory(launcherRoot);
        await CopyLauncherRootPayloadAsync(package.ExtractDirectory, sourceAppDirectory, launcherRoot, package.Version, progress, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new InstallerDeployProgress(
            "Creating deployment",
            package.Version,
            1,
            0.15,
            null,
            0,
            null));

        PrepareTargetDirectory(targetDeployment);
        await CopyDirectoryAsync(sourceAppDirectory, targetDeployment, package.Version, progress, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new InstallerDeployProgress(
            "Activating deployment",
            package.Version,
            1,
            0.92,
            null,
            0,
            null));

        ActivateInitialDeployment(launcherRoot, targetDeployment);
        CreateWindowsShortcutsIfAvailable(launcherRoot, options);

        progress?.Report(new InstallerDeployProgress(
            "Completed",
            package.Version,
            1,
            1,
            null,
            0,
            null));
    }

    public static string BuildDeploymentDirectory(string launcherRoot, string version)
    {
        var sanitized = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim();
        var index = 0;
        while (true)
        {
            var candidate = Path.Combine(launcherRoot, $"app-{sanitized}-{index}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    public static string ResolveFullPackageAppDirectory(string filesDirectory, string version)
    {
        var root = Path.GetFullPath(filesDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"PLONDS Files package directory is missing: {root}");
        }

        var executableName = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";
        var directExecutable = Path.Combine(root, executableName);
        if (File.Exists(directExecutable))
        {
            return root;
        }

        var versionDirectory = Directory
            .EnumerateDirectories(root, $"app-{version}*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => File.Exists(Path.Combine(path, executableName)));
        if (!string.IsNullOrWhiteSpace(versionDirectory))
        {
            return versionDirectory;
        }

        var nested = Directory
            .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => File.Exists(Path.Combine(path, executableName)));
        if (!string.IsNullOrWhiteSpace(nested))
        {
            return nested;
        }

        throw new FileNotFoundException($"PLONDS Files package does not contain {executableName}.");
    }

    private static void PrepareTargetDirectory(string targetDeployment)
    {
        if (Directory.Exists(targetDeployment))
        {
            Directory.Delete(targetDeployment, recursive: true);
        }

        Directory.CreateDirectory(targetDeployment);
        File.WriteAllText(Path.Combine(targetDeployment, ".partial"), string.Empty);
    }

    private static async Task CopyDirectoryAsync(
        string sourceDirectory,
        string targetDirectory,
        string version,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sourceFiles = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories).ToArray();
        var total = Math.Max(1, sourceFiles.Length);
        for (var index = 0; index < sourceFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = sourceFiles[index];
            var relativePath = InstallerPathGuard.NormalizeRelativePath(Path.GetRelativePath(sourceDirectory, sourcePath));
            if (IsDeploymentMarker(relativePath))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(targetDirectory, relativePath));
            InstallerPathGuard.EnsureChildPath(targetDirectory, targetPath);
            var targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            await using (var source = File.OpenRead(sourcePath))
            await using (var target = File.Create(targetPath))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new InstallerDeployProgress(
                "Copying files",
                version,
                1,
                0.18 + ((index + 1) * 0.70 / total),
                relativePath,
                index + 1,
                total));
        }
    }

    private static async Task CopyLauncherRootPayloadAsync(
        string packageRoot,
        string sourceAppDirectory,
        string launcherRoot,
        string version,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        var resolvedPackageRoot = Path.GetFullPath(packageRoot);
        var resolvedAppDirectory = Path.GetFullPath(sourceAppDirectory);
        if (string.Equals(
                resolvedPackageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                resolvedAppDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var files = Directory
            .EnumerateFiles(resolvedPackageRoot, "*", SearchOption.AllDirectories)
            .Where(path => !InstallerPathGuard.IsSameOrChildPath(resolvedAppDirectory, path))
            .Where(path =>
            {
                var relative = InstallerPathGuard.NormalizeRelativePath(Path.GetRelativePath(resolvedPackageRoot, path));
                return !relative.StartsWith("app-", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        var total = Math.Max(1, files.Length);
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = files[index];
            var relativePath = InstallerPathGuard.NormalizeRelativePath(Path.GetRelativePath(resolvedPackageRoot, sourcePath));
            if (IsDeploymentMarker(relativePath))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(launcherRoot, relativePath));
            InstallerPathGuard.EnsureChildPath(launcherRoot, targetPath);
            var targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            await using (var source = File.OpenRead(sourcePath))
            await using (var target = File.Create(targetPath))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new InstallerDeployProgress(
                "Copying launcher files",
                version,
                1,
                0.10 + ((index + 1) * 0.05 / total),
                relativePath,
                index + 1,
                total));
        }
    }

    private static void ActivateInitialDeployment(string launcherRoot, string targetDeployment)
    {
        foreach (var existingCurrent in Directory.EnumerateFiles(launcherRoot, ".current", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(existingCurrent);
            }
            catch
            {
            }
        }

        var partialMarker = Path.Combine(targetDeployment, ".partial");
        if (File.Exists(partialMarker))
        {
            File.Delete(partialMarker);
        }

        File.WriteAllText(Path.Combine(targetDeployment, ".current"), string.Empty);
        Directory.CreateDirectory(Path.Combine(launcherRoot, ".Launcher"));
    }

    private static long EstimateRequiredBytes(string sourceDirectory)
    {
        return Directory
            .EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
    }

    private static bool IsDeploymentMarker(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        return name is ".current" or ".partial" or ".destroy";
    }

    private static void CreateWindowsShortcutsIfAvailable(string launcherRoot, OnlineInstallOptions options)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var launcherPath = Path.Combine(launcherRoot, "LanMountainDesktop.Launcher.exe");
            if (!File.Exists(launcherPath))
            {
                var deployedLauncher = Directory
                    .EnumerateFiles(launcherRoot, "LanMountainDesktop.Launcher.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(deployedLauncher))
                {
                    File.Copy(deployedLauncher, launcherPath, overwrite: true);
                }
            }

            if (!File.Exists(launcherPath))
            {
                return;
            }

            var startMenu = InstallerElevation.IsRunningElevated()
                ? Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
                : Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            if (string.IsNullOrWhiteSpace(startMenu))
            {
                startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            }

            if (string.IsNullOrWhiteSpace(startMenu))
            {
                return;
            }

            var programs = Path.Combine(startMenu, "Programs");
            Directory.CreateDirectory(programs);
            var shortcutPath = Path.Combine(programs, "LanMountainDesktop.url");
            WriteUrlShortcut(shortcutPath, launcherPath);

            if (options.CreateDesktopShortcut)
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrWhiteSpace(desktop))
                {
                    Directory.CreateDirectory(desktop);
                    WriteUrlShortcut(Path.Combine(desktop, "LanMountainDesktop.url"), launcherPath);
                }
            }

            if (options.CreateStartupShortcut)
            {
                var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (!string.IsNullOrWhiteSpace(startup))
                {
                    Directory.CreateDirectory(startup);
                    WriteUrlShortcut(Path.Combine(startup, "LanMountainDesktop.url"), launcherPath);
                }
            }
        }
        catch
        {
            // Shortcut creation is best-effort; deployment itself must remain usable without shell integration.
        }
    }

    private static void WriteUrlShortcut(string shortcutPath, string targetPath)
    {
        File.WriteAllText(
            shortcutPath,
            $"[InternetShortcut]{Environment.NewLine}URL=file:///{targetPath.Replace('\\', '/')}{Environment.NewLine}");
    }
}
