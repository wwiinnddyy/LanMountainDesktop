using System.Security.Cryptography;
using LanMountainDesktop.Shared.Contracts.Deployment;

namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// 增量更新计划构建器：给定 PLONDS 清单的 FilesMap 和当前部署目录，
/// 计算需要替换（哈希不匹配/缺失）和删除（本地多余）的文件列表。
/// 纯逻辑，不涉及 I/O 写入。
/// </summary>
internal sealed class IncrementalPlanBuilder
{
    /// <summary>
    /// 构建增量更新计划。
    /// </summary>
    /// <param name="manifestFilesMap">清单中的完整文件映射（路径 → 文件条目含哈希）。</param>
    /// <param name="currentDeploymentDir">当前活动部署目录路径。</param>
    /// <returns>增量更新计划。</returns>
    public IncrementalUpdatePlan Build(
        IReadOnlyDictionary<string, InstallerPlondsFileEntry> manifestFilesMap,
        string currentDeploymentDir)
    {
        ArgumentNullException.ThrowIfNull(manifestFilesMap);
        if (string.IsNullOrWhiteSpace(currentDeploymentDir))
        {
            throw new ArgumentException("当前部署目录不能为空。", nameof(currentDeploymentDir));
        }

        // 检查清单中是否包含有效的逐文件哈希信息。
        // 判定规则：清单非空，但（排除部署标记后的）条目全部缺少哈希 → 无法做增量对比，回退完整更新。
        // 清单完全为空时不回退（视为无待替换文件，仍可检测本地多余文件）。
        if (manifestFilesMap.Count > 0)
        {
            var hasHashes = manifestFilesMap
                .Where(pair => !DeploymentLayout.IsDeploymentMarker(pair.Key))
                .Any(pair => !string.IsNullOrWhiteSpace(pair.Value.Hash));
            if (!hasHashes)
            {
                return IncrementalUpdatePlan.FullUpdateRequired;
            }
        }

        var filesToReplace = new List<IncrementalFileAction>();
        var filesToDelete = new List<string>();
        var filesUnchanged = new List<string>();

        // 1. 遍历清单中的每个文件，与本地文件对比
        foreach (var (relativePath, manifestEntry) in manifestFilesMap)
        {
            // 跳过部署标记文件
            if (DeploymentLayout.IsDeploymentMarker(relativePath))
            {
                continue;
            }

            var localPath = Path.Combine(currentDeploymentDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(localPath))
            {
                // 本地缺失 → 需要新增
                filesToReplace.Add(new IncrementalFileAction(
                    RelativePath: relativePath,
                    Reason: IncrementalFileReason.Missing,
                    ExpectedHash: manifestEntry.Hash,
                    ExpectedSize: manifestEntry.Size,
                    HashAlgorithm: manifestEntry.HashAlgorithm));
                continue;
            }

            // 计算本地文件哈希并与清单对比
            var localHash = ComputeFileHash(localPath, manifestEntry.HashAlgorithm);
            if (string.Equals(localHash, manifestEntry.Hash, StringComparison.OrdinalIgnoreCase))
            {
                filesUnchanged.Add(relativePath);
            }
            else
            {
                // 哈希不匹配 → 需要替换
                filesToReplace.Add(new IncrementalFileAction(
                    RelativePath: relativePath,
                    Reason: IncrementalFileReason.HashMismatch,
                    ExpectedHash: manifestEntry.Hash,
                    ExpectedSize: manifestEntry.Size,
                    HashAlgorithm: manifestEntry.HashAlgorithm));
            }
        }

        // 2. 查找本地多余文件（不在清单中）
        if (Directory.Exists(currentDeploymentDir))
        {
            var localFiles = Directory.EnumerateFiles(currentDeploymentDir, "*", SearchOption.AllDirectories);
            foreach (var localFile in localFiles)
            {
                var relativePath = Path.GetRelativePath(currentDeploymentDir, localFile)
                    .Replace(Path.DirectorySeparatorChar, '/');

                if (DeploymentLayout.IsDeploymentMarker(relativePath))
                {
                    continue;
                }

                if (!manifestFilesMap.ContainsKey(relativePath))
                {
                    filesToDelete.Add(relativePath);
                }
            }
        }

        return new IncrementalUpdatePlan(
            RequiresFullUpdate: false,
            FilesToReplace: filesToReplace,
            FilesToDelete: filesToDelete,
            FilesUnchanged: filesUnchanged);
    }

    /// <summary>
    /// 计算文件哈希值（sha256 或 md5）。
    /// </summary>
    internal static string ComputeFileHash(string filePath, string algorithm)
    {
        using HashAlgorithm hasher = algorithm?.ToLowerInvariant() switch
        {
            "md5" => MD5.Create(),
            "sha256" or "" or null => SHA256.Create(),
            _ => SHA256.Create()
        };

        using var stream = File.OpenRead(filePath);
        var hash = hasher.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// 增量更新计划。
/// </summary>
internal sealed record IncrementalUpdatePlan(
    bool RequiresFullUpdate,
    IReadOnlyList<IncrementalFileAction> FilesToReplace,
    IReadOnlyList<string> FilesToDelete,
    IReadOnlyList<string> FilesUnchanged)
{
    /// <summary>需要完整更新时的占位计划。</summary>
    public static IncrementalUpdatePlan FullUpdateRequired { get; } = new(
        RequiresFullUpdate: true,
        FilesToReplace: Array.Empty<IncrementalFileAction>(),
        FilesToDelete: Array.Empty<string>(),
        FilesUnchanged: Array.Empty<string>());
}

/// <summary>
/// 单个文件的增量操作。
/// </summary>
internal sealed record IncrementalFileAction(
    string RelativePath,
    IncrementalFileReason Reason,
    string ExpectedHash,
    long ExpectedSize,
    string HashAlgorithm);

/// <summary>
/// 增量更新中文件需要操作的原因。
/// </summary>
internal enum IncrementalFileReason
{
    /// <summary>文件在本地缺失。</summary>
    Missing,

    /// <summary>文件哈希与清单不匹配。</summary>
    HashMismatch
}
