using System.IO.Compression;
using LanDesktopPLONDS.Installer.Models;
using LanDesktopPLONDS.Installer.Services;

namespace LanDesktop.MewInstaller.Installer;

/// <summary>
/// 本地包安装：从随包的 Files.zip 直接部署，不走在线清单/下载。
/// 部署事务、快捷方式与 ARP 注册全部复用 FilesPackageInstaller。
/// </summary>
internal sealed class LocalInstallService : IOnlineInstallService
{
    private readonly string _zipPath;
    private readonly FilesPackageInstaller _packageInstaller = new();

    public LocalInstallService(string zipPath)
    {
        _zipPath = zipPath;
    }

    public Task<OnlineInstallPackageInfo> CheckLatestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_zipPath))
        {
            throw new FileNotFoundException($"未找到本地安装包：{_zipPath}。请确认 Files.zip 与安装程序在同一目录。", _zipPath);
        }

        var version = DetectVersionFromZip(_zipPath);
        return Task.FromResult(new OnlineInstallPackageInfo(
            Version: version,
            SourceId: "local-package",
            FilesZipUrl: new Uri(_zipPath),
            EstimatedBytes: new FileInfo(_zipPath).Length));
    }

    public Task InstallFreshAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken) =>
        InstallFreshAsync(installPath, OnlineInstallOptions.Default, progress, cancellationToken);

    public async Task InstallFreshAsync(
        string installPath,
        OnlineInstallOptions options,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var extractRoot = Path.Combine(
            Path.GetTempPath(),
            "LanDesktopMewInstaller",
            $"local-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(extractRoot);

        try
        {
            progress?.Report(new InstallerDeployProgress(
                "正在解压本地安装包",
                TargetVersion: null,
                DownloadProgress: 0,
                InstallProgress: 0,
                CurrentFile: Path.GetFileName(_zipPath),
                BytesDownloaded: 0,
                TotalBytes: new FileInfo(_zipPath).Length));

            ZipFile.ExtractToDirectory(_zipPath, extractRoot, overwriteFiles: true);

            var version = DetectVersionFromDirectory(extractRoot);
            var manifest = BuildLocalManifest(version);

            var package = new PreparedFilesPackage(
                Version: version,
                SourceId: "local-package",
                ZipPath: _zipPath,
                ExtractDirectory: extractRoot,
                Manifest: manifest);

            await _packageInstaller.InstallAsync(package, installPath, options, progress, cancellationToken);
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractRoot))
                {
                    Directory.Delete(extractRoot, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理失败不影响安装结果（回滚逻辑在部署事务里）。
            }
        }
    }

    public Task RepairAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken) =>
        InstallFreshAsync(installPath, OnlineInstallOptions.Default, progress, cancellationToken);

    public Task UpdateIncrementalAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken) =>
        InstallFreshAsync(installPath, OnlineInstallOptions.Default, progress, cancellationToken);

    /// <summary>从 zip 条目里解析 app-{version} 目录名得出版本号。</summary>
    private static string DetectVersionFromZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var version = ParseAppSegment(entry.FullName);
            if (version is not null)
            {
                return version;
            }
        }

        throw new InvalidDataException("本地安装包里没有找到 app-{version} 目录，无法确定版本。");
    }

    private static string DetectVersionFromDirectory(string extractRoot)
    {
        foreach (var dir in Directory.EnumerateDirectories(extractRoot, "app-*"))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith("app-", StringComparison.OrdinalIgnoreCase) && name.Length > 4)
            {
                return name[4..];
            }
        }

        throw new InvalidDataException("本地安装包解压后没有找到 app-{version} 目录，无法确定版本。");
    }

    private static string? ParseAppSegment(string entryFullName)
    {
        var segment = entryFullName.Replace('\\', '/').Split('/')[0];
        if (segment.StartsWith("app-", StringComparison.OrdinalIgnoreCase) &&
            segment.Length > 4 &&
            Version.TryParse(segment[4..], out _))
        {
            return segment[4..];
        }

        return null;
    }

    private static InstallerPlondsManifest BuildLocalManifest(string version)
    {
        return new InstallerPlondsManifest(
            FormatVersion: "2.0",
            CurrentVersion: version,
            PreviousVersion: string.Empty,
            IsFullUpdate: true,
            RequiresCleanInstall: false,
            Channel: "local",
            Platform: OperatingSystem.IsWindows() ? $"windows-{(Environment.Is64BitProcess ? "x64" : "x86")}" : "local",
            UpdatedAt: DateTimeOffset.Now,
            FilesMap: new Dictionary<string, InstallerPlondsFileEntry>(),
            ChangedFilesMap: new Dictionary<string, InstallerPlondsChangedFileEntry>(),
            Checksums: new Dictionary<string, string>(),
            Downloads: null,
            Sources: null);
    }
}
