using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.PluginUpgradeHelper;

internal static class Program
{
    private const string PendingUpgradesFileName = ".pending-plugin-upgrades.json";
    private const string LogFileName = "plugin-upgrade-helper.log";

    private static int Main(string[] args)
    {
        var logPath = Path.Combine(Path.GetTempPath(), "LanMountainDesktop", LogFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath, $"\n[{DateTime.Now:O}] PluginUpgradeHelper started. Args: {string.Join(" ", args)}\n");

        try
        {
            var parsedArgs = ParseArgs(args);

            if (!parsedArgs.TryGetValue("plugins-dir", out var pluginsDirectory) ||
                string.IsNullOrWhiteSpace(pluginsDirectory))
            {
                LogError(logPath, "Missing required argument: --plugins-dir");
                return 1;
            }

            if (!parsedArgs.TryGetValue("parent-pid", out var parentPidStr) ||
                !int.TryParse(parentPidStr, out var parentPid))
            {
                LogError(logPath, "Missing or invalid argument: --parent-pid");
                return 1;
            }

            parsedArgs.TryGetValue("launch", out var launchCommand);

            LogInfo(logPath, $"Waiting for parent process {parentPid} to exit...");
            WaitForParentProcess(parentPid);

            LogInfo(logPath, $"Processing pending upgrades in '{pluginsDirectory}'...");
            var upgradeResults = ProcessPendingUpgrades(pluginsDirectory, logPath);

            LogInfo(logPath, $"Upgrades completed. Success: {upgradeResults.SuccessCount}, Failed: {upgradeResults.FailureCount}");

            if (!string.IsNullOrWhiteSpace(launchCommand))
            {
                LogInfo(logPath, $"Launching application: {launchCommand}");
                LaunchApplication(launchCommand, parsedArgs);
            }

            return upgradeResults.FailureCount > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            LogError(logPath, $"Unexpected error: {ex}");
            return 1;
        }
    }

    private static void WaitForParentProcess(int parentPid)
    {
        try
        {
            var parentProcess = Process.GetProcessById(parentPid);
            parentProcess.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException)
        {
            // Process already exited
        }
        catch (Exception)
        {
            // Ignore errors, continue anyway
        }

        Thread.Sleep(500);
    }

    private static UpgradeResults ProcessPendingUpgrades(string pluginsDirectory, string logPath)
    {
        var pendingUpgradesPath = Path.Combine(pluginsDirectory, PendingUpgradesFileName);
        var successCount = 0;
        var failureCount = 0;

        if (!File.Exists(pendingUpgradesPath))
        {
            LogInfo(logPath, "No pending upgrades found.");
            return new UpgradeResults(0, 0);
        }

        List<PendingUpgrade>? pendingUpgrades;
        try
        {
            var json = File.ReadAllText(pendingUpgradesPath);
            pendingUpgrades = JsonSerializer.Deserialize<List<PendingUpgrade>>(json);
        }
        catch (Exception ex)
        {
            LogError(logPath, $"Failed to read pending upgrades: {ex.Message}");
            return new UpgradeResults(0, 0);
        }

        if (pendingUpgrades is null || pendingUpgrades.Count == 0)
        {
            LogInfo(logPath, "No pending upgrades to process.");
            return new UpgradeResults(0, 0);
        }

        Directory.CreateDirectory(pluginsDirectory);
        var pendingDeletionDir = Path.Combine(pluginsDirectory, ".pending-deletions");
        Directory.CreateDirectory(pendingDeletionDir);

        foreach (var upgrade in pendingUpgrades)
        {
            if (!upgrade.IsValid())
            {
                LogWarn(logPath, $"Skipping invalid upgrade entry for plugin '{upgrade.PluginId}'.");
                failureCount++;
                continue;
            }

            try
            {
                LogInfo(logPath, $"Processing upgrade for plugin '{upgrade.PluginId}' to version '{upgrade.TargetVersion}'...");

                var manifest = ReadManifestFromPackage(upgrade.SourcePackagePath);
                var destinationPath = Path.Combine(pluginsDirectory, BuildInstalledPackageFileName(manifest.Id));

                RemoveExistingPluginPackages(pluginsDirectory, manifest.Id, destinationPath, pendingDeletionDir, logPath);

                File.Copy(upgrade.SourcePackagePath, destinationPath, overwrite: true);

                LogInfo(logPath, $"Successfully upgraded plugin '{upgrade.PluginId}' to '{upgrade.TargetVersion}'.");
                successCount++;
            }
            catch (Exception ex)
            {
                LogError(logPath, $"Failed to upgrade plugin '{upgrade.PluginId}': {ex.Message}");
                failureCount++;
            }
        }

        try
        {
            File.Delete(pendingUpgradesPath);
        }
        catch (Exception ex)
        {
            LogWarn(logPath, $"Failed to delete pending upgrades file: {ex.Message}");
        }

        CleanupPendingDeletions(pendingDeletionDir, logPath);

        return new UpgradeResults(successCount, failureCount);
    }

    private static void RemoveExistingPluginPackages(
        string pluginsDirectory,
        string pluginId,
        string destinationPath,
        string pendingDeletionDir,
        string logPath)
    {
        var runtimeRootDirectory = EnsureTrailingSeparator(Path.Combine(Path.GetFullPath(pluginsDirectory), ".runtime"));

        foreach (var existingPackagePath in Directory
                     .EnumerateFiles(pluginsDirectory, "*.laapp", SearchOption.AllDirectories)
                     .Select(Path.GetFullPath)
                     .Where(path => !path.StartsWith(runtimeRootDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (string.Equals(existingPackagePath, Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var existingManifest = ReadManifestFromPackage(existingPackagePath);
                if (!string.Equals(existingManifest.Id, pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TryDeleteOrMoveFile(existingPackagePath, pendingDeletionDir, logPath);
            }
            catch
            {
                // Ignore unrelated or malformed packages
            }
        }
    }

    private static void TryDeleteOrMoveFile(string filePath, string pendingDeletionDir, string logPath)
    {
        try
        {
            File.Delete(filePath);
            LogInfo(logPath, $"Deleted existing package: {filePath}");
        }
        catch (IOException)
        {
            var fileName = Path.GetFileName(filePath);
            var pendingPath = Path.Combine(pendingDeletionDir, $"{fileName}.{Guid.NewGuid():N}.pending");
            try
            {
                File.Move(filePath, pendingPath);
                LogInfo(logPath, $"Moved existing package to pending deletion: {filePath} -> {pendingPath}");
            }
            catch (Exception ex)
            {
                LogWarn(logPath, $"Failed to move existing package '{filePath}': {ex.Message}");
            }
        }
    }

    private static void CleanupPendingDeletions(string pendingDeletionDir, string logPath)
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
            catch (Exception ex)
            {
                LogWarn(logPath, $"Failed to delete pending file '{pendingFile}': {ex.Message}");
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
            // Ignore
        }
    }

    private static void LaunchApplication(string launchCommand, Dictionary<string, string> args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launchCommand,
                UseShellExecute = true,
                WorkingDirectory = args.TryGetValue("working-dir", out var workingDir)
                    ? workingDir
                    : AppContext.BaseDirectory
            };

            if (args.TryGetValue("launch-args", out var launchArgs) && !string.IsNullOrWhiteSpace(launchArgs))
            {
                startInfo.Arguments = launchArgs;
            }

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginUpgradeHelper] Failed to launch application: {ex}");
        }
    }

    private static PluginManifest ReadManifestFromPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.Name, "plugin.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException($"Plugin package '{packagePath}' does not contain 'plugin.json'.");
        }

        if (entries.Length > 1)
        {
            throw new InvalidOperationException($"Plugin package '{packagePath}' contains multiple 'plugin.json' files.");
        }

        using var stream = entries[0].Open();
        return PluginManifest.Load(stream, $"{packagePath}!/{entries[0].FullName}");
    }

    private static string BuildInstalledPackageFileName(string pluginId)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var fileName = new string(pluginId.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return fileName + ".laapp";
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = current[2..];
            if (string.IsNullOrWhiteSpace(key) || i + 1 >= args.Length)
            {
                continue;
            }

            values[key] = args[++i];
        }

        return values;
    }

    private static void LogInfo(string logPath, string message)
    {
        File.AppendAllText(logPath, $"[{DateTime.Now:O}] [INFO] {message}\n");
    }

    private static void LogWarn(string logPath, string message)
    {
        File.AppendAllText(logPath, $"[{DateTime.Now:O}] [WARN] {message}\n");
    }

    private static void LogError(string logPath, string message)
    {
        File.AppendAllText(logPath, $"[{DateTime.Now:O}] [ERROR] {message}\n");
    }

    private sealed record PendingUpgrade(
        string PluginId,
        string SourcePackagePath,
        string TargetVersion,
        DateTimeOffset CreatedAt)
    {
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(PluginId) &&
                   !string.IsNullOrWhiteSpace(SourcePackagePath) &&
                   !string.IsNullOrWhiteSpace(TargetVersion) &&
                   File.Exists(SourcePackagePath);
        }
    }

    private sealed record UpgradeResults(int SuccessCount, int FailureCount);
}
