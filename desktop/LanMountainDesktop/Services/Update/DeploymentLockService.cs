using System;
using System.IO;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal static class DeploymentLockService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void WriteLock(string launcherRoot, DeploymentLock deploymentLock)
    {
        var lockPath = UpdatePaths.GetDeploymentLockPath(launcherRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var tempPath = lockPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(deploymentLock, JsonOptions));
        File.Move(tempPath, lockPath, true);
    }

    public static DeploymentLock? ReadLock(string launcherRoot)
    {
        var lockPath = UpdatePaths.GetDeploymentLockPath(launcherRoot);
        if (!File.Exists(lockPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(lockPath);
            return JsonSerializer.Deserialize<DeploymentLock>(json);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateLock", $"Failed to parse deployment lock: {ex.Message}");
            return null;
        }
    }

    public static void ClearLock(string launcherRoot)
    {
        var lockPath = UpdatePaths.GetDeploymentLockPath(launcherRoot);
        if (File.Exists(lockPath))
        {
            File.Delete(lockPath);
        }
    }
}
