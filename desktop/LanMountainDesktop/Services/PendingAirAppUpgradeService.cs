using System.IO.Compression;
using LanMountainDesktop.AirAppPackaging;
using LanMountainDesktop.AirApps;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Services;

public sealed class PendingAirAppUpgradeService
{
    private readonly string _airAppsDirectory;
    private readonly PendingAirAppUpgradeStore _store;
    private readonly AirAppPackageInstaller _installer = new();

    public PendingAirAppUpgradeService(string airAppsDirectory)
    {
        _airAppsDirectory = Path.GetFullPath(airAppsDirectory);
        _store = new PendingAirAppUpgradeStore(_airAppsDirectory);
    }

    public IReadOnlyList<PendingAirAppUpgrade> GetPendingUpgrades() => _store.GetPendingUpgrades();

    public void AddPendingInstallOrUpgrade(string airAppId, string sourcePackagePath, string targetVersion)
    {
        _store.AddPendingInstallOrUpgrade(airAppId, sourcePackagePath, targetVersion);
        AppLogger.Info(
            "PendingAirAppUpgrade",
            $"Added pending AirApp operation. AirAppId='{airAppId}'; TargetVersion='{targetVersion}'; Operation='{PendingAirAppOperation.InstallOrUpgrade}'; SourcePath='{sourcePackagePath}'.");
    }

    public void RemovePendingUpgrade(string airAppId)
    {
        var hadPending = _store.GetPendingUpgrades()
            .Any(u => string.Equals(u.PluginId, airAppId, StringComparison.OrdinalIgnoreCase));
        _store.RemovePendingUpgrade(airAppId);
        if (hadPending)
        {
            AppLogger.Info("PendingAirAppUpgrade", $"Removed pending upgrade. AirAppId='{airAppId}'.");
        }
    }

    public void ClearPendingUpgrades()
    {
        _store.ClearPendingUpgrades();
        AppLogger.Info("PendingAirAppUpgrade", "Cleared all pending upgrades.");
    }

    public bool HasPendingUpgrades() => _store.HasPendingUpgrades();

    public PendingAirAppOperationApplySummary ApplyPendingOperations(
        Action<AirAppManifest>? prepareManifest = null)
    {
        var pending = _store.GetPendingUpgrades();
        if (pending.Count == 0)
        {
            return new PendingAirAppOperationApplySummary(0, 0, []);
        }

        Directory.CreateDirectory(_airAppsDirectory);
        var succeeded = new List<PendingAirAppUpgrade>();
        var failures = new List<PendingAirAppOperationFailure>();

        foreach (var operation in pending)
        {
            try
            {
                if (operation.Operation != PendingAirAppOperation.InstallOrUpgrade)
                {
                    throw new InvalidOperationException($"Unsupported pending AirApp operation '{operation.Operation}'.");
                }

                var manifest = AirAppPackageReader.ReadManifest(operation.SourcePackagePath);
                prepareManifest?.Invoke(manifest);
                _installer.Install(operation.SourcePackagePath, _airAppsDirectory);
                succeeded.Add(operation);
                AppLogger.Info(
                    "PendingAirAppUpgrade",
                    $"Applied pending AirApp operation. AirAppId='{operation.PluginId}'; TargetVersion='{operation.TargetVersion}'; Operation='{operation.Operation}'.");
            }
            catch (Exception ex)
            {
                failures.Add(new PendingAirAppOperationFailure(
                    operation.PluginId,
                    operation.Operation,
                    ex.Message));
                AppLogger.Warn(
                    "PendingAirAppUpgrade",
                    $"Failed to apply pending AirApp operation. AirAppId='{operation.PluginId}'; TargetVersion='{operation.TargetVersion}'; Operation='{operation.Operation}'.",
                    ex);
            }
        }

        foreach (var operation in succeeded)
        {
            _store.RemovePendingUpgrade(operation.PluginId);
        }

        return new PendingAirAppOperationApplySummary(succeeded.Count, failures.Count, failures);
    }

}
