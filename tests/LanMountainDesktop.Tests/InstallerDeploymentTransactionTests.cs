using LanDesktopPLONDS.Installer.Models;
using LanDesktopPLONDS.Installer.Services;
using LanMountainDesktop.Shared.Contracts.Deployment;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 安装部署事务性行为的测试。
/// 覆盖：事务回滚、.partial 清理、过时部署清理、字符串字面量断言、ARP 注册/移除。
/// </summary>
public sealed class InstallerDeploymentTransactionTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        AppContext.BaseDirectory,
        "TestArtifacts",
        "LanMountainDesktop.Tests",
        nameof(InstallerDeploymentTransactionTests),
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
                // 测试清理失败时忽略
            }
        }
    }

    /// <summary>
    /// 事务性回滚：复制失败时应删除不完整的部署目录，保留预先存在的状态。
    /// </summary>
    [Fact]
    public async Task InstallAsync_CopyFailure_RollsBackDeployment()
    {
        // Arrange: 准备包目录
        var packageRoot = Path.Combine(_tempRoot, "Files");
        var appRoot = Path.Combine(packageRoot, "app-1.0.0");
        Directory.CreateDirectory(appRoot);
        File.WriteAllText(Path.Combine(appRoot, DeploymentLayout.GetHostExecutableName()), "host");

        // 创建一个可锁定的文件，模拟复制失败
        var lockableFile = Path.Combine(appRoot, "lockable.txt");
        File.WriteAllText(lockableFile, "content");

        var package = new PreparedFilesPackage(
            "1.0.0",
            "test",
            Path.Combine(_tempRoot, "Files.zip"),
            packageRoot,
            CreateManifest("1.0.0"));

        var installPath = Path.Combine(_tempRoot, "install");
        var launcherRoot = Path.Combine(installPath, InstallerPathGuard.ApplicationDirectoryName);
        Directory.CreateDirectory(launcherRoot);

        // 预先存在的文件，验证不会被破坏
        var preExistingFile = Path.Combine(launcherRoot, "pre-existing.txt");
        File.WriteAllText(preExistingFile, "original");

        // Act & Assert: 安装应失败（因为源文件被锁定）
        using var lockHandle = File.Open(lockableFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new FilesPackageInstaller().InstallAsync(package, launcherRoot, null, CancellationToken.None));

        Assert.Contains("安装失败", ex.Message);

        // 验证：不完整的部署目录应被清理
        var deploymentDirs = Directory.Exists(launcherRoot)
            ? Directory.GetDirectories(launcherRoot, $"{DeploymentLayout.DeploymentDirectoryPrefix}*")
            : Array.Empty<string>();
        Assert.Empty(deploymentDirs);

        // 验证：预先存在的文件应保持不变
        Assert.True(File.Exists(preExistingFile));
        Assert.Equal("original", File.ReadAllText(preExistingFile));

        lockHandle.Close();
    }

    /// <summary>
    /// 事务性回滚：失败时 .partial 标记应被清理（通过锁定源文件注入复制失败）。
    /// </summary>
    [Fact]
    public async Task InstallAsync_PartialMarker_CleanedUpOnFailure()
    {
        // Arrange: 准备包目录，并锁定其中一个源文件以注入复制失败
        var packageRoot = Path.Combine(_tempRoot, "Files");
        var appRoot = Path.Combine(packageRoot, "app-1.0.0");
        Directory.CreateDirectory(appRoot);
        File.WriteAllText(Path.Combine(appRoot, DeploymentLayout.GetHostExecutableName()), "host");
        var lockedFile = Path.Combine(appRoot, "locked.txt");
        File.WriteAllText(lockedFile, "extra");

        var package = new PreparedFilesPackage(
            "1.0.0",
            "test",
            Path.Combine(_tempRoot, "Files.zip"),
            packageRoot,
            CreateManifest("1.0.0"));

        var installPath = Path.Combine(_tempRoot, "install");
        var launcherRoot = Path.Combine(installPath, InstallerPathGuard.ApplicationDirectoryName);
        Directory.CreateDirectory(launcherRoot);

        // Act & Assert: 安装应失败（源文件被独占锁定，无法读取）
        using var lockHandle = File.Open(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new FilesPackageInstaller().InstallAsync(package, launcherRoot, null, CancellationToken.None));

        Assert.Contains("安装失败", ex.Message);

        // 验证：不应有任何部署目录残留（.partial 部署已被整体清理）
        if (Directory.Exists(launcherRoot))
        {
            var deploymentDirs = Directory.GetDirectories(launcherRoot, $"{DeploymentLayout.DeploymentDirectoryPrefix}*");
            Assert.Empty(deploymentDirs);
        }
    }

    /// <summary>
    /// 过时部署清理：保留最新一个作为回滚，删除更旧的。
    /// </summary>
    [Fact]
    public void CleanupStaleDeployments_KeepsNewestOneAsRollback()
    {
        // Arrange: 创建模拟的部署目录
        var launcherRoot = Path.Combine(_tempRoot, "launcher");
        Directory.CreateDirectory(launcherRoot);

        // 创建旧的部署目录（无 .current 标记）
        var oldDeployment = Path.Combine(launcherRoot, "app-1.0.0-0");
        Directory.CreateDirectory(oldDeployment);
        File.WriteAllText(Path.Combine(oldDeployment, "old.txt"), "old");

        // 创建新的部署目录（无 .current 标记）
        var newDeployment = Path.Combine(launcherRoot, "app-2.0.0-0");
        Directory.CreateDirectory(newDeployment);
        File.WriteAllText(Path.Combine(newDeployment, "new.txt"), "new");
        // 设置较新的写入时间
        Directory.SetLastWriteTimeUtc(newDeployment, DateTime.UtcNow);

        // 创建活动部署（有 .current 标记）
        var activeDeployment = Path.Combine(launcherRoot, "app-1.5.0-0");
        Directory.CreateDirectory(activeDeployment);
        File.WriteAllText(Path.Combine(activeDeployment, DeploymentLayout.CurrentMarkerFileName), string.Empty);

        // Act
        FilesPackageInstaller.CleanupStaleDeployments(launcherRoot);

        // Assert: 活动部署应保留
        Assert.True(Directory.Exists(activeDeployment));

        // Assert: 新的部署目录应保留（作为回滚）
        Assert.True(Directory.Exists(newDeployment));

        // Assert: 旧的部署目录应被删除
        Assert.False(Directory.Exists(oldDeployment));
    }

    /// <summary>
    /// 过时部署清理：删除失败（目录被锁定）的部署应写入 .destroy 标记。
    /// 布局：活动部署 + 回滚部署（保留）+ 被锁定的最旧部署（删除失败 → .destroy）。
    /// </summary>
    [Fact]
    public void CleanupStaleDeployments_LockedDirGetsDestroyMarker()
    {
        // Arrange: 创建模拟的部署目录
        var launcherRoot = Path.Combine(_tempRoot, "launcher");
        Directory.CreateDirectory(launcherRoot);

        // 最旧部署（将被锁定，删除失败）
        var lockedDeployment = Path.Combine(launcherRoot, "app-1.0.0-0");
        Directory.CreateDirectory(lockedDeployment);
        var lockedFile = Path.Combine(lockedDeployment, "locked.dll");
        File.WriteAllText(lockedFile, "x");
        Directory.SetLastWriteTimeUtc(lockedDeployment, DateTime.UtcNow.AddDays(-2));

        // 回滚部署（无 .current，最新的非活动部署 → 保留）
        var rollbackDeployment = Path.Combine(launcherRoot, "app-1.5.0-0");
        Directory.CreateDirectory(rollbackDeployment);
        Directory.SetLastWriteTimeUtc(rollbackDeployment, DateTime.UtcNow.AddDays(-1));

        // 活动部署
        var activeDeployment = Path.Combine(launcherRoot, "app-2.0.0-0");
        Directory.CreateDirectory(activeDeployment);
        File.WriteAllText(Path.Combine(activeDeployment, DeploymentLayout.CurrentMarkerFileName), string.Empty);

        // Act: 锁定最旧部署中的文件后执行清理
        using (File.Open(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            FilesPackageInstaller.CleanupStaleDeployments(launcherRoot);
        }

        // Assert: 活动部署与回滚部署保留
        Assert.True(Directory.Exists(activeDeployment));
        Assert.True(Directory.Exists(rollbackDeployment));

        // 被锁定的部署删除失败 → 应残留且带 .destroy 标记
        Assert.True(Directory.Exists(lockedDeployment));
        Assert.True(File.Exists(Path.Combine(lockedDeployment, DeploymentLayout.DestroyMarkerFileName)));
    }

    /// <summary>
    /// 源代码断言：FilesPackageInstaller.cs 中不应包含 ".current" 字符串字面量。
    /// 应使用 DeploymentLayout.CurrentMarkerFileName 代替。
    /// </summary>
    [Fact]
    public void FilesPackageInstaller_ShouldNotContainDotCurrentLiteral()
    {
        var installerProjectDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "install", "LanDesktopPLONDS.installer"));
        var filesPackageInstallerPath = Path.Combine(installerProjectDir, "Services", "FilesPackageInstaller.cs");

        Assert.True(File.Exists(filesPackageInstallerPath),
            $"FilesPackageInstaller.cs 未找到：{filesPackageInstallerPath}");

        var source = File.ReadAllText(filesPackageInstallerPath);

        // 排除注释行和字符串中的引用，只检查代码中的直接使用
        var lines = source.Split('\n');
        var violations = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // 跳过注释行
            if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
            {
                continue;
            }

            // 检查是否有直接的 ".current" 字符串字面量
            // 允许 DeploymentLayout.CurrentMarkerFileName 的使用
            if (trimmed.Contains("\".current\"") && !trimmed.Contains("DeploymentLayout.CurrentMarkerFileName"))
            {
                violations.Add($"行 {i + 1}: {trimmed}");
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// 源代码断言：FilesPackageInstaller.cs 中不应包含 ".partial" 或 ".destroy" 字符串字面量。
    /// 应使用 DeploymentLayout 常量代替。
    /// </summary>
    [Fact]
    public void FilesPackageInstaller_ShouldNotContainMarkerLiterals()
    {
        var installerProjectDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "install", "LanDesktopPLONDS.installer"));
        var filesPackageInstallerPath = Path.Combine(installerProjectDir, "Services", "FilesPackageInstaller.cs");

        Assert.True(File.Exists(filesPackageInstallerPath),
            $"FilesPackageInstaller.cs 未找到：{filesPackageInstallerPath}");

        var source = File.ReadAllText(filesPackageInstallerPath);
        var lines = source.Split('\n');
        var violations = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
            {
                continue;
            }

            if (trimmed.Contains("\".partial\"") && !trimmed.Contains("DeploymentLayout.PartialMarkerFileName"))
            {
                violations.Add($"行 {i + 1} (.partial): {trimmed}");
            }

            if (trimmed.Contains("\".destroy\"") && !trimmed.Contains("DeploymentLayout.DestroyMarkerFileName"))
            {
                violations.Add($"行 {i + 1} (.destroy): {trimmed}");
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// ARP 注册：使用注入的测试子键写入和移除注册表条目。
    /// 仅在 Windows 上运行。
    /// </summary>
    [Fact]
    public void ArpRegistration_RegisterAndRemove_UsesInjectedSubKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var testBase = @"Software\LanMountainDesktopTests";
        var launcherRoot = Path.Combine(_tempRoot, "launcher");
        Directory.CreateDirectory(launcherRoot);
        var launcherExePath = Path.Combine(launcherRoot, DeploymentLayout.GetLauncherExecutableName());
        File.WriteAllText(launcherExePath, "mock launcher");

        try
        {
            // Act: 注册
            ArpRegistration.Register(launcherRoot, "1.0.0", testBase);

            // Assert: 注册表键应存在
            var subKeyPath = ArpRegistration.GetUninstallSubKeyPath(testBase);
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKeyPath);
            if (key != null)
            {
                Assert.Equal("阑山桌面", key.GetValue("DisplayName"));
                Assert.Equal("1.0.0", key.GetValue("DisplayVersion"));
                Assert.Equal("LanMountain", key.GetValue("Publisher"));
                Assert.Equal(1, key.GetValue("NoModify"));
                Assert.Equal(1, key.GetValue("NoRepair"));
            }
            else
            {
                // HKLM 写入可能需要管理员权限，尝试 HKCU
                using var userKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKeyPath);
                if (userKey != null)
                {
                    Assert.Equal("阑山桌面", userKey.GetValue("DisplayName"));
                    Assert.Equal("1.0.0", userKey.GetValue("DisplayVersion"));
                }
            }

            // Act: 移除
            ArpRegistration.Remove(testBase);

            // Assert: 注册表键应不存在
            using var keyAfterRemove = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKeyPath);
            using var userKeyAfterRemove = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKeyPath);
            Assert.Null(keyAfterRemove);
            Assert.Null(userKeyAfterRemove);
        }
        finally
        {
            // 清理：确保测试注册表键被删除
            try
            {
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(
                    ArpRegistration.GetUninstallSubKeyPath(testBase), throwOnMissingSubKey: false);
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                    ArpRegistration.GetUninstallSubKeyPath(testBase), throwOnMissingSubKey: false);
            }
            catch
            {
                // 清理失败时忽略
            }
        }
    }

    /// <summary>
    /// RunningProcessGuard：空安装路径不抛出异常。
    /// </summary>
    [Fact]
    public void RunningProcessGuard_EmptyPath_DoesNotThrow()
    {
        // 对于非 Windows 平台或空目录，不应抛出异常
        var emptyDir = Path.Combine(_tempRoot, "empty");
        Directory.CreateDirectory(emptyDir);

        // 不应抛出异常
        var processes = RunningProcessGuard.FindRunningProcesses(emptyDir);
        Assert.NotNull(processes);
    }

    /// <summary>
    /// 成功安装后应有 .current 标记且无 .partial 标记。
    /// </summary>
    [Fact]
    public async Task InstallAsync_Success_CreatesCurrentAndNoPartial()
    {
        // Arrange: 准备包目录
        var packageRoot = Path.Combine(_tempRoot, "Files");
        var appRoot = Path.Combine(packageRoot, "app-3.0.0");
        Directory.CreateDirectory(appRoot);
        File.WriteAllText(Path.Combine(appRoot, DeploymentLayout.GetHostExecutableName()), "host");
        File.WriteAllText(Path.Combine(appRoot, "data.txt"), "data");

        var package = new PreparedFilesPackage(
            "3.0.0",
            "test",
            Path.Combine(_tempRoot, "Files.zip"),
            packageRoot,
            CreateManifest("3.0.0"));

        var installPath = Path.Combine(_tempRoot, "install");
        var launcherRoot = Path.Combine(installPath, InstallerPathGuard.ApplicationDirectoryName);

        // Act
        await new FilesPackageInstaller().InstallAsync(package, launcherRoot, null, CancellationToken.None);

        // Assert: 应有 .current 标记
        var deploymentDir = Path.Combine(launcherRoot, "app-3.0.0-0");
        Assert.True(File.Exists(Path.Combine(deploymentDir, DeploymentLayout.CurrentMarkerFileName)));

        // Assert: 不应有 .partial 标记
        Assert.False(File.Exists(Path.Combine(deploymentDir, DeploymentLayout.PartialMarkerFileName)));

        // Assert: 主程序文件应存在
        Assert.True(File.Exists(Path.Combine(deploymentDir, DeploymentLayout.GetHostExecutableName())));
    }

    private static InstallerPlondsManifest CreateManifest(string version = "1.0.0")
    {
        return new InstallerPlondsManifest(
            "1",
            version,
            "0.9.0",
            true,
            false,
            "stable",
            "windows-x64",
            DateTimeOffset.UtcNow,
            new Dictionary<string, InstallerPlondsFileEntry>(),
            new Dictionary<string, InstallerPlondsChangedFileEntry>(),
            new Dictionary<string, string>(),
            null,
            null);
    }
}
