using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.AirAppPackaging;

public enum PendingAirAppOperation
{
    InstallOrUpgrade = 0
}

// PluginId 成员名即 .pending-plugin-upgrades.json 的磁盘字段名（SerializerOptions 未设
// PropertyNamingPolicy，按成员名原样序列化）。改它会读不到已安装实例里排队的升级，冻结。
// 下方 PendingAirAppOperationFailure.PluginId 同理。
public sealed record PendingAirAppUpgrade(
    string PluginId,
    string SourcePackagePath,
    string TargetVersion,
    DateTimeOffset CreatedAt,
    PendingAirAppOperation Operation = PendingAirAppOperation.InstallOrUpgrade)
{
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(PluginId) &&
               !string.IsNullOrWhiteSpace(SourcePackagePath) &&
               !string.IsNullOrWhiteSpace(TargetVersion);
    }
}

public sealed record PendingAirAppOperationApplySummary(
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<PendingAirAppOperationFailure> Failures);

public sealed record PendingAirAppOperationFailure(
    string PluginId,
    PendingAirAppOperation Operation,
    string ErrorMessage);

public sealed class PendingAirAppUpgradeStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _airAppsDirectory;
    private readonly string _pendingUpgradesFilePath;
    private readonly object _gate = new();

    public PendingAirAppUpgradeStore(string airAppsDirectory)
    {
        _airAppsDirectory = Path.GetFullPath(airAppsDirectory);
        _pendingUpgradesFilePath = Path.Combine(_airAppsDirectory, AirAppPackagingConstants.PendingUpgradesFileName);
    }

    public IReadOnlyList<PendingAirAppUpgrade> GetPendingUpgrades()
    {
        lock (_gate)
        {
            return ReadPendingUpgradesCore();
        }
    }

    public void AddPendingInstallOrUpgrade(string airAppId, string sourcePackagePath, string targetVersion)
    {
        AddPendingOperation(airAppId, sourcePackagePath, targetVersion, PendingAirAppOperation.InstallOrUpgrade);
    }

    public void RemovePendingUpgrade(string airAppId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(airAppId);

        lock (_gate)
        {
            var upgrades = ReadPendingUpgradesCore().ToList();
            var removed = upgrades.RemoveAll(u =>
                string.Equals(u.PluginId, airAppId, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                SavePendingUpgradesCore(upgrades);
            }
        }
    }

    public void ClearPendingUpgrades()
    {
        lock (_gate)
        {
            if (File.Exists(_pendingUpgradesFilePath))
            {
                File.Delete(_pendingUpgradesFilePath);
            }
        }
    }

    public bool HasPendingUpgrades()
    {
        lock (_gate)
        {
            return ReadPendingUpgradesCore().Count > 0;
        }
    }

    public PendingAirAppOperationApplySummary ApplyPendingOperations(
        AirAppPackageInstaller installer,
        AirAppPackageInstallOptions? options = null,
        Action<AirAppPackageManifest>? prepareManifest = null)
    {
        options ??= AirAppPackageInstallOptions.Default;

        lock (_gate)
        {
            var pending = ReadPendingUpgradesCore();
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

                    installer.Install(operation.SourcePackagePath, _airAppsDirectory, options, prepareManifest);
                    succeeded.Add(operation);
                }
                catch (Exception ex)
                {
                    failures.Add(new PendingAirAppOperationFailure(
                        operation.PluginId,
                        operation.Operation,
                        ex.Message));
                }
            }

            var remaining = pending.Except(succeeded).ToList();
            SavePendingUpgradesCore(remaining);
            return new PendingAirAppOperationApplySummary(succeeded.Count, failures.Count, failures);
        }
    }

    private void AddPendingOperation(
        string airAppId,
        string sourcePackagePath,
        string targetVersion,
        PendingAirAppOperation operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(airAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVersion);

        lock (_gate)
        {
            var upgrades = ReadPendingUpgradesCore().ToList();
            upgrades.RemoveAll(u =>
                string.Equals(u.PluginId, airAppId, StringComparison.OrdinalIgnoreCase));

            upgrades.Add(new PendingAirAppUpgrade(
                airAppId,
                Path.GetFullPath(sourcePackagePath),
                targetVersion,
                DateTimeOffset.UtcNow,
                operation));

            SavePendingUpgradesCore(upgrades);
        }
    }

    private List<PendingAirAppUpgrade> ReadPendingUpgradesCore()
    {
        if (!File.Exists(_pendingUpgradesFilePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_pendingUpgradesFilePath);
            var upgrades = JsonSerializer.Deserialize<List<PendingAirAppUpgrade>>(json, SerializerOptions);
            return upgrades?.Where(u => u.IsValid()).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SavePendingUpgradesCore(List<PendingAirAppUpgrade> upgrades)
    {
        var directory = Path.GetDirectoryName(_pendingUpgradesFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (upgrades.Count == 0)
        {
            if (File.Exists(_pendingUpgradesFilePath))
            {
                File.Delete(_pendingUpgradesFilePath);
            }

            return;
        }

        var json = JsonSerializer.Serialize(upgrades, SerializerOptions);
        File.WriteAllText(_pendingUpgradesFilePath, json);
    }
}
