using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class PluginUpgradeQueueService
{
    private const string PendingUpgradesFileName = ".pending-plugin-upgrades.json";

    private readonly PluginInstallerService _installerService;

    public PluginUpgradeQueueService(PluginInstallerService installerService)
    {
        _installerService = installerService;
    }

    public LauncherResult ApplyPendingUpgrades(string pluginsDirectory)
    {
        var pendingPath = Path.Combine(pluginsDirectory, PendingUpgradesFileName);
        if (!File.Exists(pendingPath))
        {
            return new LauncherResult
            {
                Success = true,
                Stage = "plugin.update",
                Code = "noop",
                Message = "No pending plugin upgrades."
            };
        }

        var text = File.ReadAllText(pendingPath);
        var pending = JsonSerializer.Deserialize(text, AppJsonContext.Default.ListPendingUpgrade) ?? [];
        var failures = new List<string>();
        var succeeded = new List<PendingUpgrade>();

        foreach (var item in pending)
        {
            if (!item.IsValid())
            {
                failures.Add(item.PluginId);
                continue;
            }

            try
            {
                _installerService.InstallPackage(item.SourcePackagePath, pluginsDirectory);
                succeeded.Add(item);
            }
            catch
            {
                failures.Add(item.PluginId);
            }
        }

        var remaining = pending
            .Except(succeeded)
            .Where(item => failures.Contains(item.PluginId, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (remaining.Count == 0)
        {
            File.Delete(pendingPath);
        }
        else
        {
            File.WriteAllText(pendingPath, JsonSerializer.Serialize(remaining, AppJsonContext.Default.ListPendingUpgrade));
        }

        return new LauncherResult
        {
            Success = failures.Count == 0,
            Stage = "plugin.update",
            Code = failures.Count == 0 ? "ok" : "partial_failed",
            Message = failures.Count == 0
                ? $"Applied {succeeded.Count} pending plugin upgrade(s)."
                : $"Applied {succeeded.Count} upgrades, failed: {string.Join(", ", failures)}."
        };
    }
}

internal sealed record PendingUpgrade(
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
