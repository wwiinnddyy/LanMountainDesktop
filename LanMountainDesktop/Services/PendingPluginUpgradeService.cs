using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace LanMountainDesktop.Services;

public sealed class PendingPluginUpgradeService
{
    private const string PendingUpgradesFileName = ".pending-plugin-upgrades.json";
    private static readonly Lock Gate = new();
    private readonly string _pendingUpgradesFilePath;

    public PendingPluginUpgradeService(string pluginsDirectory)
    {
        _pendingUpgradesFilePath = Path.Combine(pluginsDirectory, PendingUpgradesFileName);
    }

    public IReadOnlyList<PendingPluginUpgrade> GetPendingUpgrades()
    {
        lock (Gate)
        {
            return ReadPendingUpgradesCore();
        }
    }

    public void AddPendingUpgrade(string pluginId, string sourcePackagePath, string targetVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVersion);

        lock (Gate)
        {
            var upgrades = ReadPendingUpgradesCore().ToList();

            upgrades.RemoveAll(u =>
                string.Equals(u.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));

            upgrades.Add(new PendingPluginUpgrade(
                pluginId,
                Path.GetFullPath(sourcePackagePath),
                targetVersion,
                DateTimeOffset.UtcNow));

            SavePendingUpgradesCore(upgrades);

            AppLogger.Info(
                "PendingPluginUpgrade",
                $"Added pending upgrade. PluginId='{pluginId}'; TargetVersion='{targetVersion}'; SourcePath='{sourcePackagePath}'.");
        }
    }

    public void RemovePendingUpgrade(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        lock (Gate)
        {
            var upgrades = ReadPendingUpgradesCore().ToList();
            var removed = upgrades.RemoveAll(u =>
                string.Equals(u.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                SavePendingUpgradesCore(upgrades);
                AppLogger.Info(
                    "PendingPluginUpgrade",
                    $"Removed pending upgrade. PluginId='{pluginId}'.");
            }
        }
    }

    public void ClearPendingUpgrades()
    {
        lock (Gate)
        {
            if (File.Exists(_pendingUpgradesFilePath))
            {
                File.Delete(_pendingUpgradesFilePath);
                AppLogger.Info("PendingPluginUpgrade", "Cleared all pending upgrades.");
            }
        }
    }

    public bool HasPendingUpgrades()
    {
        lock (Gate)
        {
            return ReadPendingUpgradesCore().Count > 0;
        }
    }

    private List<PendingPluginUpgrade> ReadPendingUpgradesCore()
    {
        if (!File.Exists(_pendingUpgradesFilePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_pendingUpgradesFilePath);
            var upgrades = JsonSerializer.Deserialize<List<PendingPluginUpgrade>>(json);
            return upgrades?.Where(u => u.IsValid()).ToList() ?? [];
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "PendingPluginUpgrade",
                $"Failed to read pending upgrades from '{_pendingUpgradesFilePath}'.",
                ex);
            return [];
        }
    }

    private void SavePendingUpgradesCore(List<PendingPluginUpgrade> upgrades)
    {
        try
        {
            var directory = Path.GetDirectoryName(_pendingUpgradesFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(upgrades, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_pendingUpgradesFilePath, json);
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "PendingPluginUpgrade",
                $"Failed to save pending upgrades to '{_pendingUpgradesFilePath}'.",
                ex);
            throw;
        }
    }
}

public sealed record PendingPluginUpgrade(
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
