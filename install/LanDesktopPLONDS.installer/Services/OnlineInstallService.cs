using LanDesktopPLONDS.Installer.Models;
using LanMountainDesktop.Shared.Data;
using LanMountainDesktop.Shared.Contracts.Deployment;
using LanMountainDesktop.Shared.Contracts.Privacy;

namespace LanDesktopPLONDS.Installer.Services;

internal sealed class OnlineInstallService(
    InstallerPlondsClient plondsClient,
    FilesPackageInstaller packageInstaller,
    IPrivacyDeviceIdentityProvider privacyIdentity,
    InstalledProductInspector installedProductInspector,
    IncrementalPlanBuilder incrementalPlanBuilder,
    Func<PreparedFilesPackage, CancellationToken, Task>? ensureRuntime = null) : IOnlineInstallService
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
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DeploymentLayout.HostExecutableBaseName);
        var stagingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UserDataRoot.FolderName,
            "Installer",
            "PLONDS");
        return new OnlineInstallService(
            new InstallerPlondsClient(httpClient, stagingRoot),
            new FilesPackageInstaller(),
            privacyIdentity,
            new InstalledProductInspector(),
            new IncrementalPlanBuilder(), RuntimePrerequisite.EnsureAsync);
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
        if (ensureRuntime is not null) await ensureRuntime(package, cancellationToken).ConfigureAwait(false);
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
        if (ensureRuntime is not null) await ensureRuntime(package, cancellationToken).ConfigureAwait(false);

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
        if (ensureRuntime is not null) await ensureRuntime(package, cancellationToken).ConfigureAwait(false);

        // All deployment paths share verification, root-payload copying, and marker commit.
        if (candidate.Manifest.RequiresCleanInstall || candidate.Manifest.IsFullUpdate || plan.RequiresFullUpdate)
            await packageInstaller.InstallAsync(package, launcherRoot, progress, cancellationToken).ConfigureAwait(false);
        else
            await packageInstaller.InstallIncrementalAsync(package, launcherRoot, installed.DeploymentPath, plan, progress, cancellationToken).ConfigureAwait(false);
    }
}
