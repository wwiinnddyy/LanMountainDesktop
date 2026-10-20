using System.Diagnostics;
using LanDesktopPLONDS.Installer.Models;
using LanMountainDesktop.Shared.Contracts.Deployment;
using LanMountainDesktop.Shared.IO;

namespace LanDesktopPLONDS.Installer.Services;

internal sealed class FilesPackageInstaller
{
    private readonly Action<string, string> _registerInstallation;

    public FilesPackageInstaller(Action<string, string>? registerInstallation = null)
    {
        _registerInstallation = registerInstallation ?? ((root, version) => ArpRegistration.Register(root, version));
    }

    public Task InstallAsync(
        PreparedFilesPackage package, string installPath,
        IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken) =>
        InstallAsync(package, installPath, OnlineInstallOptions.Default, progress, cancellationToken);

    public Task InstallAsync(
        PreparedFilesPackage package, string installPath, OnlineInstallOptions options,
        IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken) =>
        InstallCoreAsync(package, installPath, options, progress, cancellationToken, null, null);

    internal Task InstallIncrementalAsync(
        PreparedFilesPackage package, string installPath, string currentDeployment, IncrementalUpdatePlan plan,
        IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken) =>
        InstallCoreAsync(package, installPath, OnlineInstallOptions.Default, progress, cancellationToken, currentDeployment, plan);

    private async Task InstallCoreAsync(
        PreparedFilesPackage package, string installPath, OnlineInstallOptions options,
        IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken,
        string? currentDeployment, IncrementalUpdatePlan? plan)
    {
        ArgumentNullException.ThrowIfNull(package);

        var launcherRoot = InstallerPathGuard.NormalizeInstallPath(installPath);
        var sourceAppDirectory = ResolveFullPackageAppDirectory(package.ExtractDirectory, package.Version);
        var targetDeployment = DeploymentLayout.BuildDeploymentDirectory(launcherRoot, package.Version);

        InstallerElevation.EnsureCanInstall(launcherRoot);
        InstallerPathGuard.EnsureUsableInstallPath(launcherRoot, EstimateRequiredBytes(sourceAppDirectory));
        Directory.CreateDirectory(launcherRoot);

        // 在复制 launcherRoot 文件之前，检测是否有进程正在运行
        RunningProcessGuard.EnsureNoRunningProcesses(launcherRoot);
        using var rootTransaction = new DeploymentRootFileTransaction(launcherRoot);
        var committed = false;

        // 事务性安装：失败时回滚，不破坏已有状态
        try
        {
            // (a) 创建部署目录并写入 .partial 标记
            progress?.Report(new InstallerDeployProgress(
                "创建部署目录",
                package.Version,
                1,
                0.12,
                null,
                0,
                null));

            DeploymentStaging.Prepare(targetDeployment);

            // (b) 复制所有应用文件到部署目录
            await CopyDirectoryAsync(sourceAppDirectory, targetDeployment, package.Version, progress, cancellationToken, currentDeployment, plan)
                .ConfigureAwait(false);

            // (c) 验证文件数量匹配
            progress?.Report(new InstallerDeployProgress(
                "验证文件完整性",
                package.Version,
                1,
                0.85,
                null,
                0,
                null));

            VerifyFileCount(sourceAppDirectory, targetDeployment);
            DeploymentValidation.Verify(targetDeployment,
                package.Manifest.FilesMap.Select(pair => new DeploymentFileHash(
                    pair.Key, pair.Value.Action, pair.Value.Hash, pair.Value.HashAlgorithm, pair.Value.Size)),
                cancellationToken);

            // (d) 复制 launcherRoot 载荷：先写入临时兄弟目录，再逐文件原子移入
            progress?.Report(new InstallerDeployProgress(
                "复制启动器文件",
                package.Version,
                1,
                0.88,
                null,
                0,
                null));

            rootTransaction.Copy(package.ExtractDirectory, sourceAppDirectory, cancellationToken);

            // (e) 激活部署：删除 .partial → 写 .current → 清除其他 .current
            progress?.Report(new InstallerDeployProgress(
                "激活部署",
                package.Version,
                1,
                0.92,
                null,
                0,
                null));

            rootTransaction.PrepareCommit();
            ActivateDeployment(launcherRoot, targetDeployment, rootTransaction.BackupDirectory);
            rootTransaction.Commit();
            committed = true;
            CreateWindowsShortcutsIfAvailable(launcherRoot, options);

            // ARP 注册（仅 Windows）
            _registerInstallation(launcherRoot, package.Version);

            // 清理过时部署目录，保留最新一个作为回滚
            CleanupStaleDeployments(launcherRoot);

            progress?.Report(new InstallerDeployProgress(
                "已完成",
                package.Version,
                1,
                1,
                null,
                0,
                null));
        }
        catch (Exception ex)
        {
            if (committed)
            {
                InstallerStartupDiagnostics.Log($"Deployment was committed; optional registration or cleanup failed: {ex.Message}");
                return;
            }
            // (f) 任何失败：删除不完整的部署目录，保留预先存在的状态，重新抛出带中文上下文的异常
            if (!committed) CleanupPartialDeployment(targetDeployment);
            throw new InvalidOperationException($"安装失败，已回滚更改：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 从包目录中解析出包含主程序可执行文件的应用目录。
    /// </summary>
    public static string ResolveFullPackageAppDirectory(string filesDirectory, string version)
    {
        var root = Path.GetFullPath(filesDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"PLONDS Files 包目录不存在：{root}");
        }

        var executableName = DeploymentLayout.GetHostExecutableName();
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

        throw new FileNotFoundException($"PLONDS Files 包中未找到 {executableName}。");
    }

    /// <summary>
    /// 准备目标部署目录：创建目录并写入 .partial 标记。
    /// </summary>
    /// <summary>
    /// 逐文件复制源目录到目标目录，跳过标记文件。
    /// </summary>
    private static async Task CopyDirectoryAsync(
        string sourceDirectory,
        string targetDirectory,
        string version,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken,
        string? currentDeployment,
        IncrementalUpdatePlan? plan)
    {
        var sourceFiles = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories).ToArray();
        var total = Math.Max(1, sourceFiles.Length);
        for (var index = 0; index < sourceFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = sourceFiles[index];
            var relativePath = InstallerPathGuard.NormalizeRelativePath(Path.GetRelativePath(sourceDirectory, sourcePath));
            if (DeploymentLayout.IsDeploymentMarker(relativePath))
            {
                continue;
            }
            if (currentDeployment is not null && plan is not null && plan.FilesUnchanged.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
                sourcePath = Path.Combine(currentDeployment, relativePath.Replace('/', Path.DirectorySeparatorChar));

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
                "复制文件",
                version,
                1,
                0.15 + ((index + 1) * 0.70 / total),
                relativePath,
                index + 1,
                total));
        }
    }

    /// <summary>
    /// 验证源目录与目标目录的文件数量匹配。
    /// </summary>
    private static void VerifyFileCount(string sourceDirectory, string targetDirectory)
    {
        var sourceFiles = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Count(p => !DeploymentLayout.IsDeploymentMarker(
                InstallerPathGuard.NormalizeRelativePath(Path.GetRelativePath(sourceDirectory, p))));
        var targetFiles = Directory.EnumerateFiles(targetDirectory, "*", SearchOption.AllDirectories)
            .Count(p => !DeploymentLayout.IsDeploymentMarker(
                InstallerPathGuard.NormalizeRelativePath(Path.GetRelativePath(targetDirectory, p))));

        if (sourceFiles != targetFiles)
        {
            throw new InvalidOperationException(
                $"文件数量验证失败：源目录 {sourceFiles} 个文件，目标目录 {targetFiles} 个文件。");
        }
    }

    /// <summary>
    /// 激活部署：删除 .partial → 写 .current → 移除其他部署的 .current → 创建 .Launcher 目录。
    /// </summary>
    private static void ActivateDeployment(string launcherRoot, string targetDeployment, string rootBackupDirectory)
    {
        DeploymentCommit.Activate(launcherRoot, targetDeployment, rootBackupDirectory);
        Directory.CreateDirectory(Path.Combine(launcherRoot, DeploymentLayout.LauncherStateDirectoryName));
    }

    /// <summary>
    /// 清理不完整的部署目录（回滚用）。
    /// </summary>
    private static void CleanupPartialDeployment(string targetDeployment)
    {
        try
        {
            if (Directory.Exists(targetDeployment))
            {
                Directory.Delete(targetDeployment, recursive: true);
            }
        }
        catch
        {
            // 回滚失败时尝试标记 .destroy
            try
            {
                if (Directory.Exists(targetDeployment))
                {
                    File.WriteAllText(
                        Path.Combine(targetDeployment, DeploymentLayout.DestroyMarkerFileName),
                        string.Empty);
                }
            }
            catch
            {
                // 最终放弃，无法清理
            }
        }
    }

    /// <summary>
    /// 清理过时的部署目录：保留最新一个作为回滚，删除更旧的；锁定的目录写入 .destroy 标记。
    /// </summary>
    public static void CleanupStaleDeployments(string launcherRoot)
    {
        var deployments = Directory.EnumerateDirectories(launcherRoot)
            .Where(dir =>
            {
                var name = Path.GetFileName(dir);
                return DeploymentLayout.IsDeploymentDirectoryName(name);
            })
            .OrderByDescending(dir => Directory.GetLastWriteTimeUtc(dir))
            .ToList();

        var keptRollback = false;
        foreach (var deployment in deployments)
        {
            var hasCurrent = File.Exists(Path.Combine(deployment, DeploymentLayout.CurrentMarkerFileName));
            if (hasCurrent)
            {
                // 活动部署不处理
                continue;
            }

            if (!keptRollback)
            {
                // 保留最新一个作为回滚
                keptRollback = true;
                continue;
            }

            // 尝试删除更旧的部署
            try
            {
                Directory.Delete(deployment, recursive: true);
            }
            catch
            {
                // 目录被锁定时写入 .destroy 标记
                try
                {
                    File.WriteAllText(
                        Path.Combine(deployment, DeploymentLayout.DestroyMarkerFileName),
                        string.Empty);
                }
                catch
                {
                    // 无法标记也放弃
                }
            }
        }
    }

    private static long EstimateRequiredBytes(string sourceDirectory)
    {
        return Directory
            .EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
    }

    private static void CreateWindowsShortcutsIfAvailable(string launcherRoot, OnlineInstallOptions options)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var launcherExeName = DeploymentLayout.GetLauncherExecutableName();
            var launcherPath = Path.Combine(launcherRoot, launcherExeName);
            if (!File.Exists(launcherPath))
            {
                var deployedLauncher = Directory
                    .EnumerateFiles(launcherRoot, launcherExeName, SearchOption.AllDirectories)
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
            WindowsShortcutWriter.WriteUrlShortcut(shortcutPath, launcherPath);

            if (options.CreateDesktopShortcut)
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrWhiteSpace(desktop))
                {
                    Directory.CreateDirectory(desktop);
                    WindowsShortcutWriter.WriteUrlShortcut(Path.Combine(desktop, "LanMountainDesktop.url"), launcherPath);
                }
            }

            if (options.CreateStartupShortcut)
            {
                var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (!string.IsNullOrWhiteSpace(startup))
                {
                    Directory.CreateDirectory(startup);
                    WindowsShortcutWriter.WriteUrlShortcut(Path.Combine(startup, "LanMountainDesktop.url"), launcherPath);
                }
            }
        }
        catch
        {
            // 快捷方式创建是尽力而为；部署本身必须在没有 shell 集成的情况下可用。
        }
    }
}
