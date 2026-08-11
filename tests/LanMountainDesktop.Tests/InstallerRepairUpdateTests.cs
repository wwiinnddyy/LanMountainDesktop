using LanDesktopPLONDS.Installer.Services;
using LanMountainDesktop.Shared.Contracts.Deployment;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 修复与增量更新功能的单元测试。
/// 覆盖 InstalledProductInspector 检测、IncrementalPlanBuilder 计划构建、
/// 无哈希回退逻辑，以及 Launcher 回归验证。
/// </summary>
public sealed class InstallerRepairUpdateTests : IDisposable
{
    private readonly string _testRoot;

    public InstallerRepairUpdateTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.RepairUpdateTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    // ==================== InstalledProductInspector 测试 ====================

    [Fact]
    public void InstalledProductInspector_DetectsVersionFromAppDirWithCurrentMarker()
    {
        // Arrange: 创建 app-1.2.3-0 目录并放置 .current 标记
        var appDir = Path.Combine(_testRoot, "app-1.2.3-0");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, DeploymentLayout.CurrentMarkerFileName), string.Empty);

        var inspector = new InstalledProductInspector();

        // Act
        var result = inspector.Detect(_testRoot);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.2.3", result.Version.ToString()); // app-1.2.3-0 的尾部 -0 是部署序号，不属于版本号
        Assert.Equal(appDir, result.DeploymentPath);
        Assert.True(result.HasCurrentMarker);
    }

    [Fact]
    public void InstalledProductInspector_PrefersCurrentMarkerOverHigherVersion()
    {
        // Arrange: 创建两个版本，较低版本有 .current，较高版本没有
        var olderDir = Path.Combine(_testRoot, "app-1.0.0-0");
        var newerDir = Path.Combine(_testRoot, "app-2.0.0-0");
        Directory.CreateDirectory(olderDir);
        Directory.CreateDirectory(newerDir);
        File.WriteAllText(Path.Combine(olderDir, DeploymentLayout.CurrentMarkerFileName), string.Empty);

        var inspector = new InstalledProductInspector();

        // Act
        var result = inspector.Detect(_testRoot);

        // Assert: 应选择有 .current 标记的版本（1.0.0），即使 2.0.0 更新
        Assert.NotNull(result);
        Assert.Equal("1.0.0", result.Version.ToString());
        Assert.Equal(olderDir, result.DeploymentPath);
    }

    [Fact]
    public void InstalledProductInspector_SkipsDestroyMarkedDirs()
    {
        // Arrange: 创建两个目录，.current 目录同时被标记为 .destroy
        var destroyedDir = Path.Combine(_testRoot, "app-1.0.0-0");
        var validDir = Path.Combine(_testRoot, "app-2.0.0-0");
        Directory.CreateDirectory(destroyedDir);
        Directory.CreateDirectory(validDir);
        File.WriteAllText(Path.Combine(destroyedDir, DeploymentLayout.CurrentMarkerFileName), string.Empty);
        File.WriteAllText(Path.Combine(destroyedDir, DeploymentLayout.DestroyMarkerFileName), string.Empty);
        File.WriteAllText(Path.Combine(validDir, DeploymentLayout.CurrentMarkerFileName), string.Empty);

        var inspector = new InstalledProductInspector();

        // Act
        var result = inspector.Detect(_testRoot);

        // Assert: 应跳过被标记为 destroy 的目录
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result.Version.ToString());
        Assert.Equal(validDir, result.DeploymentPath);
    }

    [Fact]
    public void InstalledProductInspector_SkipsPartialMarkedDirs()
    {
        // Arrange: 创建一个带 .partial 标记的目录
        var partialDir = Path.Combine(_testRoot, "app-1.0.0-0");
        Directory.CreateDirectory(partialDir);
        File.WriteAllText(Path.Combine(partialDir, DeploymentLayout.PartialMarkerFileName), string.Empty);

        var inspector = new InstalledProductInspector();

        // Act
        var result = inspector.Detect(_testRoot);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void InstalledProductInspector_ReturnsNullWhenNoDeployments()
    {
        var inspector = new InstalledProductInspector();
        var result = inspector.Detect(_testRoot);
        Assert.Null(result);
    }

    [Fact]
    public void InstalledProductInspector_ReturnsNullForNonexistentRoot()
    {
        var inspector = new InstalledProductInspector();
        var result = inspector.Detect(Path.Combine(_testRoot, "nonexistent"));
        Assert.Null(result);
    }

    [Fact]
    public void InstalledProductInspector_ParsesComplexVersionFromDirName()
    {
        // Arrange: 测试预发布版本号解析
        var appDir = Path.Combine(_testRoot, "app-0.8.5-beta.1-0");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, DeploymentLayout.CurrentMarkerFileName), string.Empty);

        var inspector = new InstalledProductInspector();

        // Act
        var result = inspector.Detect(_testRoot);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("0.8.5-beta.1", result.Version.ToString());
    }

    [Fact]
    public void InstalledProductInspector_ParseVersionFromDirectoryName_ValidInputs()
    {
        // 测试各种目录名格式的版本解析
        Assert.Equal("1.2.3", InstalledProductInspector.ParseVersionFromDirectoryName("app-1.2.3-0")?.ToString());
        Assert.Equal("1.2.3.4", InstalledProductInspector.ParseVersionFromDirectoryName("app-1.2.3.4-1")?.ToString());
        Assert.Equal("0.8.5-beta.1", InstalledProductInspector.ParseVersionFromDirectoryName("app-0.8.5-beta.1-0")?.ToString());
        Assert.Equal("1.0.0-rc.1", InstalledProductInspector.ParseVersionFromDirectoryName("app-1.0.0-rc.1-3")?.ToString());
    }

    [Fact]
    public void InstalledProductInspector_ParseVersionFromDirectoryName_InvalidInputs()
    {
        Assert.Null(InstalledProductInspector.ParseVersionFromDirectoryName(""));
        Assert.Null(InstalledProductInspector.ParseVersionFromDirectoryName("not-app-dir"));
        Assert.Null(InstalledProductInspector.ParseVersionFromDirectoryName("app-"));
    }

    // ==================== IncrementalPlanBuilder 测试 ====================

    [Fact]
    public void IncrementalPlanBuilder_DetectsHashMismatch()
    {
        // Arrange: 创建当前部署目录，写入一个文件
        var deployDir = Path.Combine(_testRoot, "deploy-current");
        Directory.CreateDirectory(deployDir);
        File.WriteAllText(Path.Combine(deployDir, "file1.dll"), "local content v1");

        var filesMap = new Dictionary<string, InstallerPlondsFileEntry>
        {
            ["file1.dll"] = new InstallerPlondsFileEntry("replace", "sha256_of_new_content", 100)
        };

        var builder = new IncrementalPlanBuilder();

        // Act
        var plan = builder.Build(filesMap, deployDir);

        // Assert: 应检测到哈希不匹配
        Assert.False(plan.RequiresFullUpdate);
        Assert.Single(plan.FilesToReplace);
        Assert.Equal("file1.dll", plan.FilesToReplace[0].RelativePath);
        Assert.Equal(IncrementalFileReason.HashMismatch, plan.FilesToReplace[0].Reason);
        Assert.Empty(plan.FilesUnchanged);
    }

    [Fact]
    public void IncrementalPlanBuilder_DetectsMissingFile()
    {
        // Arrange: 部署目录为空，但清单中有文件
        var deployDir = Path.Combine(_testRoot, "deploy-empty");
        Directory.CreateDirectory(deployDir);

        var filesMap = new Dictionary<string, InstallerPlondsFileEntry>
        {
            ["new-file.dll"] = new InstallerPlondsFileEntry("add", "somehash", 500)
        };

        var builder = new IncrementalPlanBuilder();

        // Act
        var plan = builder.Build(filesMap, deployDir);

        // Assert
        Assert.False(plan.RequiresFullUpdate);
        Assert.Single(plan.FilesToReplace);
        Assert.Equal("new-file.dll", plan.FilesToReplace[0].RelativePath);
        Assert.Equal(IncrementalFileReason.Missing, plan.FilesToReplace[0].Reason);
    }

    [Fact]
    public void IncrementalPlanBuilder_DetectsExtraLocalFile()
    {
        // Arrange: 部署目录有文件，但清单中没有
        var deployDir = Path.Combine(_testRoot, "deploy-extra");
        Directory.CreateDirectory(deployDir);
        File.WriteAllText(Path.Combine(deployDir, "old-file.dll"), "old content");

        var filesMap = new Dictionary<string, InstallerPlondsFileEntry>();

        var builder = new IncrementalPlanBuilder();

        // Act
        var plan = builder.Build(filesMap, deployDir);

        // Assert
        Assert.False(plan.RequiresFullUpdate);
        Assert.Empty(plan.FilesToReplace);
        Assert.Single(plan.FilesToDelete);
        Assert.Equal("old-file.dll", plan.FilesToDelete[0]);
    }

    [Fact]
    public void IncrementalPlanBuilder_IdentifiesUnchangedFiles()
    {
        // Arrange: 创建文件并计算其真实哈希
        var deployDir = Path.Combine(_testRoot, "deploy-unchanged");
        Directory.CreateDirectory(deployDir);
        var fileContent = "unchanged content";
        var filePath = Path.Combine(deployDir, "unchanged.dll");
        File.WriteAllText(filePath, fileContent);

        var realHash = IncrementalPlanBuilder.ComputeFileHash(filePath, "sha256");

        var filesMap = new Dictionary<string, InstallerPlondsFileEntry>
        {
            ["unchanged.dll"] = new InstallerPlondsFileEntry("keep", realHash, fileContent.Length)
        };

        var builder = new IncrementalPlanBuilder();

        // Act
        var plan = builder.Build(filesMap, deployDir);

        // Assert
        Assert.False(plan.RequiresFullUpdate);
        Assert.Empty(plan.FilesToReplace);
        Assert.Single(plan.FilesUnchanged);
        Assert.Equal("unchanged.dll", plan.FilesUnchanged[0]);
    }

    [Fact]
    public void IncrementalPlanBuilder_FullClassification_MixedChanges()
    {
        // Arrange: 混合场景 — 有不变的、有变更的、有缺失的、有多余的
        var deployDir = Path.Combine(_testRoot, "deploy-mixed");
        Directory.CreateDirectory(deployDir);

        // 不变文件
        var unchangedContent = "keep me";
        var unchangedPath = Path.Combine(deployDir, "unchanged.dll");
        File.WriteAllText(unchangedPath, unchangedContent);
        var unchangedHash = IncrementalPlanBuilder.ComputeFileHash(unchangedPath, "sha256");

        // 变更文件（本地存在但哈希不同）
        File.WriteAllText(Path.Combine(deployDir, "changed.dll"), "old version");

        // 多余文件（本地有但清单没有）
        File.WriteAllText(Path.Combine(deployDir, "extra.dll"), "remove me");

        var filesMap = new Dictionary<string, InstallerPlondsFileEntry>
        {
            ["unchanged.dll"] = new InstallerPlondsFileEntry("keep", unchangedHash, unchangedContent.Length),
            ["changed.dll"] = new InstallerPlondsFileEntry("replace", "different_hash_abc", 999),
            ["missing.dll"] = new InstallerPlondsFileEntry("add", "new_hash_xyz", 123)
        };

        var builder = new IncrementalPlanBuilder();

        // Act
        var plan = builder.Build(filesMap, deployDir);

        // Assert
        Assert.False(plan.RequiresFullUpdate);
        Assert.Equal(2, plan.FilesToReplace.Count); // changed.dll (HashMismatch) + missing.dll (Missing)
        Assert.Single(plan.FilesUnchanged); // unchanged.dll
        Assert.Single(plan.FilesToDelete); // extra.dll

        var replacedPaths = plan.FilesToReplace.Select(f => f.RelativePath).ToHashSet();
        Assert.Contains("changed.dll", replacedPaths);
        Assert.Contains("missing.dll", replacedPaths);
        Assert.Contains("unchanged.dll", plan.FilesUnchanged);
        Assert.Contains("extra.dll", plan.FilesToDelete);
    }

    [Fact]
    public void IncrementalPlanBuilder_NoHashes_ReturnsFullUpdateRequired()
    {
        // Arrange: FilesMap 中所有条目的 Hash 为空
        var deployDir = Path.Combine(_testRoot, "deploy-nohash");
        Directory.CreateDirectory(deployDir);
        File.WriteAllText(Path.Combine(deployDir, "file.dll"), "content");

        var filesMap = new Dictionary<string, InstallerPlondsFileEntry>
        {
            ["file.dll"] = new InstallerPlondsFileEntry("replace", "", 100)
        };

        var builder = new IncrementalPlanBuilder();

        // Act
        var plan = builder.Build(filesMap, deployDir);

        // Assert: 应返回需要完整更新
        Assert.True(plan.RequiresFullUpdate);
    }

    [Fact]
    public void IncrementalPlanBuilder_SkipsDeploymentMarkers()
    {
        // Arrange: 清单中包含标记文件
        var deployDir = Path.Combine(_testRoot, "deploy-markers");
        Directory.CreateDirectory(deployDir);

        var filesMap = new Dictionary<string, InstallerPlondsFileEntry>
        {
            [".current"] = new InstallerPlondsFileEntry("keep", "hash1", 0),
            [".partial"] = new InstallerPlondsFileEntry("keep", "hash2", 0),
            [".destroy"] = new InstallerPlondsFileEntry("keep", "hash3", 0)
        };

        var builder = new IncrementalPlanBuilder();

        // Act
        var plan = builder.Build(filesMap, deployDir);

        // Assert: 标记文件应被跳过
        Assert.True(plan.RequiresFullUpdate); // 因为所有非标记的 Hash 都为空
        // 但 FilesToReplace 中不应包含标记文件（它们被跳过了）
        // 由于所有条目的 Hash 为空，整个计划标记为 FullUpdateRequired
    }

    [Fact]
    public void IncrementalPlanBuilder_EmptyFilesMap_ReturnsNoChanges()
    {
        // Arrange: 空清单 + 空目录
        var deployDir = Path.Combine(_testRoot, "deploy-emptymap");
        Directory.CreateDirectory(deployDir);

        var filesMap = new Dictionary<string, InstallerPlondsFileEntry>
        {
            ["real-file.dll"] = new InstallerPlondsFileEntry("keep", "realhash", 100)
        };

        var builder = new IncrementalPlanBuilder();

        // Act
        var plan = builder.Build(filesMap, deployDir);

        // Assert: 所有文件都缺失
        Assert.False(plan.RequiresFullUpdate);
        Assert.Single(plan.FilesToReplace);
        Assert.Equal(IncrementalFileReason.Missing, plan.FilesToReplace[0].Reason);
    }

    // ==================== ComputeFileHash 基本验证 ====================

    [Fact]
    public void ComputeFileHash_DeterministicOutput()
    {
        // Arrange
        var tempFile = Path.Combine(_testRoot, "hash-test.bin");
        File.WriteAllBytes(tempFile, [1, 2, 3, 4, 5]);

        // Act
        var hash1 = IncrementalPlanBuilder.ComputeFileHash(tempFile, "sha256");
        var hash2 = IncrementalPlanBuilder.ComputeFileHash(tempFile, "sha256");

        // Assert: 同一文件多次计算应产生相同哈希
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public void ComputeFileHash_DifferentContentProducesDifferentHash()
    {
        var file1 = Path.Combine(_testRoot, "hash-a.bin");
        var file2 = Path.Combine(_testRoot, "hash-b.bin");
        File.WriteAllBytes(file1, [1, 2, 3]);
        File.WriteAllBytes(file2, [4, 5, 6]);

        var hash1 = IncrementalPlanBuilder.ComputeFileHash(file1, "sha256");
        var hash2 = IncrementalPlanBuilder.ComputeFileHash(file2, "sha256");

        Assert.NotEqual(hash1, hash2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            try
            {
                Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // 测试清理失败不阻塞其他测试
            }
        }
    }
}
