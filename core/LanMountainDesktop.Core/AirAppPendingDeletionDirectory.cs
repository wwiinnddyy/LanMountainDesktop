using System;
using System.IO;

using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.AirAppPackaging;

/// <summary>
/// AirApp 安装期"待删除包"暂存目录（<see cref="AirAppPackagingConstants.PendingDeletionDirectoryName"/>）
/// 唯一的一处收尾：清掉 <c>*.pending</c> 标记，目录空了就把目录本身也收掉。
///
/// 收到一处之前有 3 份实现、且**它们做的不是同一件事**：Core 的 <c>AirAppPackageInstaller</c> 与
/// 启动器的 <c>AirAppInstallerService</c> 各抄一份"只删标记"（14 行逐字相同），
/// 宿主的 <c>AirAppRuntimeService.CleanupPendingDeletionDirectory</c> 多一段"空目录顺手删掉"。
/// 也就是说同一个隐藏目录，走安装器留下空壳、走运行时会被回收——漂移的症状不是报错，
/// 是 <c>AirApps</c> 目录里时有时无一个空文件夹（枚举安装包时会把它算进遍历路径）。
/// 这里取**超集那条**：目录只在需要时被重建，没人依赖它一直存在。
/// </summary>
public static class AirAppPendingDeletionDirectory
{
    public static string PathFor(string airAppsDirectory) =>
        Path.Combine(airAppsDirectory, AirAppPackagingConstants.PendingDeletionDirectoryName);

    /// <summary>
    /// 装同一个 id 时怎么处理旧包文件：先带重试地删；删不动（被瞬时锁占住）就改名挪进本目录，
    /// 等这一次安装的收尾 <see cref="CleanupAfterInstall"/> 再回收。
    /// </summary>
    /// <remarks>
    /// 收到这里之前，Core 的 <c>AirAppPackageInstaller</c> 与启动器的 <c>AirAppInstallerService</c>
    /// 各抄了一份逐字相同的 13 行（2026-09-24 由普查尺子量出）。漂开的后果不报错：只有一侧改成
    /// "删不动就挪进来"，另一侧照旧把"删不动"当失败往上抛，用户看到的就成了"重装同一个轻应用，
    /// 有时成功有时报安装失败"。挪进来的文件名带一个 GUID 段，是因为两次安装可能同时想挪同一个包。
    /// <paramref name="pendingDeletionDir"/> 由调用方保证已经建好（两个调用点都在循环前先 CreateDirectory）。
    /// </remarks>
    public static void RemoveOrMoveToPending(string existingPackagePath, string pendingDeletionDir, string category)
    {
        try
        {
            FileOperationRetryHelper.DeleteFileWithRetry(existingPackagePath, category);
        }
        catch (IOException)
        {
            var fileName = Path.GetFileName(existingPackagePath);
            var pendingPath = Path.Combine(pendingDeletionDir, $"{fileName}.{Guid.NewGuid():N}.pending");
            File.Move(existingPackagePath, pendingPath);
        }
    }

    /// <summary>清理暂存目录里的 <c>*.pending</c> 标记；目录因此变空时一并删掉。尽力而为，失败不抛。</summary>
    public static void CleanupAfterInstall(string pendingDeletionDir)
    {
        if (!Directory.Exists(pendingDeletionDir))
        {
            return;
        }

        foreach (var pendingFile in Directory.EnumerateFiles(pendingDeletionDir, "*.pending"))
        {
            try
            {
                File.Delete(pendingFile);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        try
        {
            if (Directory.GetFiles(pendingDeletionDir).Length == 0 &&
                Directory.GetDirectories(pendingDeletionDir).Length == 0)
            {
                Directory.Delete(pendingDeletionDir);
            }
        }
        catch
        {
            // 目录正被别人用着：留着下次收。
        }
    }
}
