using System.IO.Compression;
using LanMountainDesktop.PluginPackaging;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

public sealed class PendingPluginUpgradeService
{
    private readonly string _pluginsDirectory;
    private readonly PendingPluginUpgradeStore _store;
    private readonly PluginPackageInstaller _installer = new();

    public PendingPluginUpgradeService(string pluginsDirectory)
    {
        _pluginsDirectory = Path.GetFullPath(pluginsDirectory);
        _store = new PendingPluginUpgradeStore(_pluginsDirectory);
    }

    public IReadOnlyList<PendingPluginUpgrade> GetPendingUpgrades() => _store.GetPendingUpgrades();

    public void AddPendingUpgrade(string pluginId, string sourcePackagePath, string targetVersion)
    {
        AddPendingInstallOrUpgrade(pluginId, sourcePackagePath, targetVersion);
    }

    public void AddPendingInstallOrUpgrade(string pluginId, string sourcePackagePath, string targetVersion)
    {
        _store.AddPendingInstallOrUpgrade(pluginId, sourcePackagePath, targetVersion);
        AppLogger.Info(
            "PendingPluginUpgrade",
            $"Added pending plugin operation. PluginId='{pluginId}'; TargetVersion='{targetVersion}'; Operation='{PendingPluginOperation.InstallOrUpgrade}'; SourcePath='{sourcePackagePath}'.");
    }

    public void RemovePendingUpgrade(string pluginId)
    {
        var hadPending = _store.GetPendingUpgrades()
            .Any(u => string.Equals(u.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));
        _store.RemovePendingUpgrade(pluginId);
        if (hadPending)
        {
            AppLogger.Info("PendingPluginUpgrade", $"Removed pending upgrade. PluginId='{pluginId}'.");
        }
    }

    public void ClearPendingUpgrades()
    {
        _store.ClearPendingUpgrades();
        AppLogger.Info("PendingPluginUpgrade", "Cleared all pending upgrades.");
    }

    public bool HasPendingUpgrades() => _store.HasPendingUpgrades();

    public PendingPluginOperationApplySummary ApplyPendingOperations(
        Action<PluginManifest>? prepareManifest = null)
    {
        var pending = _store.GetPendingUpgrades();
        if (pending.Count == 0)
        {
            return new PendingPluginOperationApplySummary(0, 0, []);
        }

        Directory.CreateDirectory(_pluginsDirectory);
        var succeeded = new List<PendingPluginUpgrade>();
        var failures = new List<PendingPluginOperationFailure>();

        foreach (var operation in pending)
        {
            try
            {
                if (operation.Operation != PendingPluginOperation.InstallOrUpgrade)
                {
                    throw new InvalidOperationException($"Unsupported pending plugin operation '{operation.Operation}'.");
                }

                var manifest = ReadManifestFromPackage(operation.SourcePackagePath);
                prepareManifest?.Invoke(manifest);
                _installer.Install(operation.SourcePackagePath, _pluginsDirectory);
                succeeded.Add(operation);
                AppLogger.Info(
                    "PendingPluginUpgrade",
                    $"Applied pending plugin operation. PluginId='{operation.PluginId}'; TargetVersion='{operation.TargetVersion}'; Operation='{operation.Operation}'.");
            }
            catch (Exception ex)
            {
                failures.Add(new PendingPluginOperationFailure(
                    operation.PluginId,
                    operation.Operation,
                    ex.Message));
                AppLogger.Warn(
                    "PendingPluginUpgrade",
                    $"Failed to apply pending plugin operation. PluginId='{operation.PluginId}'; TargetVersion='{operation.TargetVersion}'; Operation='{operation.Operation}'.",
                    ex);
            }
        }

        foreach (var operation in succeeded)
        {
            _store.RemovePendingUpgrade(operation.PluginId);
        }

        return new PendingPluginOperationApplySummary(succeeded.Count, failures.Count, failures);
    }

    private static PluginManifest ReadManifestFromPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.Name, PluginSdkInfo.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException($"Plugin package '{packagePath}' does not contain '{PluginSdkInfo.ManifestFileName}'.");
        }

        if (entries.Length > 1)
        {
            throw new InvalidOperationException($"Plugin package '{packagePath}' contains multiple '{PluginSdkInfo.ManifestFileName}' files.");
        }

        using var stream = entries[0].Open();
        return PluginManifest.Load(stream, $"{packagePath}!/{entries[0].FullName}");
    }
}
