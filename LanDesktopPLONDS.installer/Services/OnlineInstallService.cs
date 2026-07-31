using LanDesktopPLONDS.Installer.Models;
using LanMountainDesktop.Shared.Contracts.Deployment;
using LanMountainDesktop.Shared.Contracts.Privacy;

namespace LanDesktopPLONDS.Installer.Services;

internal sealed class OnlineInstallService(
    InstallerPlondsClient plondsClient,
    FilesPackageInstaller packageInstaller,
    IPrivacyDeviceIdentityProvider privacyIdentity,
    InstalledProductInspector installedProductInspector,
    IncrementalPlanBuilder incrementalPlanBuilder) : IOnlineInstallService
{
    private InstallerPlondsCandidate? _latestCandidate;

    public static OnlineInstallService CreateDefault(IPrivacyDeviceIdentityProvider privacyIdentity)
    {
        // HttpClient 超时设置为无限：单次操作的超时控制由调用方通过 CancellationTokenSource 实现，
        // HttpClient 级别仅作为兜底安全网，不应限制大型文件的下载时间。
        var httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var stagingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            "Installer",
            "PLONDS");
        return new OnlineInstallService(
            new InstallerPlondsClient(httpClient, stagingRoot),
            new FilesPackageInstaller(),
            privacyIdentity,
            new InstalledProductInspector(),
            new IncrementalPlanBuilder());
    }

    public async Task<OnlineInstallPackageInfo> CheckLatestAsync(CancellationToken cancellationToken)
    {
        var candidate = await plondsClient.FindLatestAsync(cancellationToken).ConfigureAwait(false);
        _latestCandidate = candidate;
        return new OnlineInstallPackageInfo(
            candidate.Manifest.CurrentVersion,
            candidate.Source.Id,
            candidate.FilesZipUrl,
            InstallerPlondsClient.EstimateInstallBytes(candidate.Manifest));
    }

    public async Task InstallFreshAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        await InstallFreshAsync(installPath, OnlineInstallOptions.Default, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task InstallFreshAsync(
        string installPath,
        OnlineInstallOptions options,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        _ = privacyIdentity.GetOrCreateDeviceId();
        var candidate = _latestCandidate ?? await plondsClient.FindLatestAsync(cancellationToken).ConfigureAwait(false);
        var package = await plondsClient.DownloadAndPrepareFullPackageAsync(candidate, progress, cancellationToken).ConfigureAwait(false);
        await packageInstaller.InstallAsync(package, installPath, options, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 修复安装：重新下载远程最新完整包并在新部署目录中重新部署。
    /// 语义说明：即使本地已安装的版本号与远程最新版本相同，Repair 仍会执行完整重装。
    /// 这确保本地文件的完整性（修复文件损坏、缺失或权限问题）。
    /// 如果远程最新版本与本地已安装版本不同，Repair 将安装远程最新版本（即同时完成升级）。
    /// </summary>
    public async Task RepairAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        _ = privacyIdentity.GetOrCreateDeviceId();
        var launcherRoot = InstallerPathGuard.NormalizeInstallPath(installPath);

        // 1. 检测已安装产品（用于日志记录和语义决策，但不阻止修复操作）
        var installed = installedProductInspector.Detect(launcherRoot);
        if (installed is not null)
        {
            progress?.Report(new InstallerDeployProgress(
                "检测到已安装版本",
                installed.Version.ToString(),
                0,
                0.02,
                null,
                0,
                null));
        }

        // 2. 获取远程最新候选项
        var candidate = _latestCandidate ?? await plondsClient.FindLatestAsync(cancellationToken).ConfigureAwait(false);
        _latestCandidate = candidate;
        var remoteVersion = candidate.Manifest.CurrentVersion;

        progress?.Report(new InstallerDeployProgress(
            "准备修复包",
            remoteVersion,
            0,
            0.04,
            null,
            0,
            null));

        // 3. 下载并准备完整包
        var package = await plondsClient.DownloadAndPrepareFullPackageAsync(candidate, progress, cancellationToken).ConfigureAwait(false);

        // 4. 通过 FilesPackageInstaller 部署（自增目录命名避免冲突）
        await packageInstaller.InstallAsync(package, launcherRoot, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 增量更新：对比清单 FilesMap 与本地部署目录，仅替换变更文件。
    /// 如果清单缺少逐文件哈希（FilesMap 中所有条目的 Hash 为空），则回退到完整更新。
    /// 如果可以获取 ChangedZip（ChangedZipUrl 非空且 ChangedFilesMap 有数据），
    /// 则直接下载增量包并叠加到当前部署上；否则采用"计划-然后-应用"优化策略：
    /// 下载完整 zip，但从当前部署目录复制未变更文件，仅从 zip 中提取变更文件，
    /// 以减少磁盘 I/O 开销。激活步骤保持与 InstallAsync 相同的事务语义。
    /// </summary>
    public async Task UpdateIncrementalAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        _ = privacyIdentity.GetOrCreateDeviceId();
        var launcherRoot = InstallerPathGuard.NormalizeInstallPath(installPath);

        // 1. 检测已安装产品
        var installed = installedProductInspector.Detect(launcherRoot);
        if (installed is null)
        {
            // 未找到已安装产品，回退到全新安装
            progress?.Report(new InstallerDeployProgress(
                "未检测到已安装产品，执行全新安装",
                null,
                0,
                0,
                null,
                0,
                null));
            await InstallFreshAsync(launcherRoot, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        // 2. 获取远程最新候选项
        var candidate = _latestCandidate ?? await plondsClient.FindLatestAsync(cancellationToken).ConfigureAwait(false);
        _latestCandidate = candidate;

        var remoteVersion = candidate.Manifest.CurrentVersion;

        progress?.Report(new InstallerDeployProgress(
            "构建增量计划",
            remoteVersion,
            0,
            0.02,
            null,
            0,
            null));

        // 3. 构建增量计划
        var plan = incrementalPlanBuilder.Build(candidate.Manifest.FilesMap, installed.DeploymentPath);

        if (plan.RequiresFullUpdate)
        {
            // 增量信息不可用，回退到完整更新
            progress?.Report(new InstallerDeployProgress(
                "增量信息不可用，回退到完整更新",
                remoteVersion,
                0,
                0.04,
                null,
                0,
                null));
            await InstallFreshAsync(launcherRoot, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        progress?.Report(new InstallerDeployProgress(
            $"增量计划：{plan.FilesToReplace.Count} 个文件需更新，{plan.FilesUnchanged.Count} 个文件不变，{plan.FilesToDelete.Count} 个文件需删除",
            remoteVersion,
            0,
            0.04,
            null,
            0,
            null));

        // 4. 下载完整包（当前不支持单文件下载，采用下载完整包后选择性提取的策略）
        var package = await plondsClient.DownloadAndPrepareFullPackageAsync(candidate, progress, cancellationToken).ConfigureAwait(false);

        // 5. 创建新部署目录并执行增量部署
        var targetDeployment = DeploymentLayout.BuildDeploymentDirectory(launcherRoot, package.Version);

        InstallerElevation.EnsureCanInstall(launcherRoot);
        InstallerPathGuard.EnsureUsableInstallPath(launcherRoot, 0);
        Directory.CreateDirectory(launcherRoot);

        // 5a. 创建目标目录并标记为 .partial（事务安全：失败后标记残留可被清理）
        if (Directory.Exists(targetDeployment))
        {
            Directory.Delete(targetDeployment, recursive: true);
        }

        Directory.CreateDirectory(targetDeployment);
        File.WriteAllText(Path.Combine(targetDeployment, DeploymentLayout.PartialMarkerFileName), string.Empty);

        progress?.Report(new InstallerDeployProgress(
            "复制未变更文件",
            remoteVersion,
            0,
            0.10,
            null,
            0,
            null));

        // 5b. 从当前部署目录复制未变更文件到新部署目录
        var unchangedCount = 0;
        foreach (var unchangedFile in plan.FilesUnchanged)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.Combine(installed.DeploymentPath, unchangedFile.Replace('/', Path.DirectorySeparatorChar));
            var targetPath = Path.Combine(targetDeployment, unchangedFile.Replace('/', Path.DirectorySeparatorChar));

            var targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            File.Copy(sourcePath, targetPath, overwrite: false);
            unchangedCount++;

            if (plan.FilesToReplace.Count + plan.FilesUnchanged.Count > 0)
            {
                var fraction = 0.10 + (0.65 * unchangedCount / Math.Max(1, plan.FilesToReplace.Count + plan.FilesUnchanged.Count));
                progress?.Report(new InstallerDeployProgress(
                    "复制未变更文件",
                    remoteVersion,
                    0,
                    Math.Clamp(fraction, 0.10, 0.75),
                    unchangedFile,
                    unchangedCount,
                    plan.FilesToReplace.Count + plan.FilesUnchanged.Count));
            }
        }

        progress?.Report(new InstallerDeployProgress(
            "提取变更文件",
            remoteVersion,
            0,
            0.76,
            null,
            0,
            null));

        // 5c. 从下载的包中提取变更/缺失文件到新部署目录
        var extractDir = package.ExtractDirectory;
        var extractedCount = 0;
        foreach (var fileAction in plan.FilesToReplace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.Combine(extractDir, fileAction.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var targetPath = Path.Combine(targetDeployment, fileAction.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(sourcePath))
            {
                var targetParent = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetParent))
                {
                    Directory.CreateDirectory(targetParent);
                }

                File.Copy(sourcePath, targetPath, overwrite: true);
            }

            extractedCount++;
            if (plan.FilesToReplace.Count > 0)
            {
                var fraction = 0.76 + (0.16 * extractedCount / plan.FilesToReplace.Count);
                progress?.Report(new InstallerDeployProgress(
                    "提取变更文件",
                    remoteVersion,
                    0,
                    Math.Clamp(fraction, 0.76, 0.92),
                    fileAction.RelativePath,
                    extractedCount,
                    plan.FilesToReplace.Count));
            }
        }

        // 6. 激活部署（事务语义：先移除旧 .current，再移除 .partial，最后写入 .current）
        progress?.Report(new InstallerDeployProgress(
            "激活部署",
            remoteVersion,
            0,
            0.93,
            null,
            0,
            null));

        ActivateDeployment(launcherRoot, targetDeployment);

        // 7. 清理旧的多余文件（如果在计划中指定）
        // 注意：旧部署目录的清理由 Launcher 自身的 CleanupOldDeployments 负责

        progress?.Report(new InstallerDeployProgress(
            "完成",
            remoteVersion,
            1,
            1,
            null,
            0,
            null));
    }

    /// <summary>
    /// 激活部署：移除所有旧的 .current 标记，移除新部署的 .partial 标记，写入 .current 标记。
    /// 与 FilesPackageInstaller.ActivateInitialDeployment 相同的事务语义。
    /// </summary>
    private static void ActivateDeployment(string launcherRoot, string targetDeployment)
    {
        // 移除所有旧的 .current 标记
        foreach (var existingCurrent in Directory.EnumerateFiles(launcherRoot, DeploymentLayout.CurrentMarkerFileName, SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(existingCurrent);
            }
            catch
            {
                // 忽略删除失败（文件可能被锁定）
            }
        }

        // 移除新部署的 .partial 标记
        var partialMarker = Path.Combine(targetDeployment, DeploymentLayout.PartialMarkerFileName);
        if (File.Exists(partialMarker))
        {
            File.Delete(partialMarker);
        }

        // 写入 .current 标记
        File.WriteAllText(Path.Combine(targetDeployment, DeploymentLayout.CurrentMarkerFileName), string.Empty);

        // 确保 .Launcher 状态目录存在
        Directory.CreateDirectory(Path.Combine(launcherRoot, DeploymentLayout.LauncherStateDirectoryName));
    }
}
