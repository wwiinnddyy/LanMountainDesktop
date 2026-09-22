using System;
using System.IO;

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
