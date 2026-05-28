using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.PluginPackaging;

public enum PendingPluginOperation
{
    InstallOrUpgrade = 0
}

public sealed record PendingPluginUpgrade(
    string PluginId,
    string SourcePackagePath,
    string TargetVersion,
    DateTimeOffset CreatedAt,
    PendingPluginOperation Operation = PendingPluginOperation.InstallOrUpgrade)
{
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(PluginId) &&
               !string.IsNullOrWhiteSpace(SourcePackagePath) &&
               !string.IsNullOrWhiteSpace(TargetVersion);
    }
}

public sealed record PendingPluginOperationApplySummary(
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<PendingPluginOperationFailure> Failures);

public sealed record PendingPluginOperationFailure(
    string PluginId,
    PendingPluginOperation Operation,
    string ErrorMessage);

public sealed class PendingPluginUpgradeStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _pluginsDirectory;
    private readonly string _pendingUpgradesFilePath;
    private readonly object _gate = new();

    public PendingPluginUpgradeStore(string pluginsDirectory)
    {
        _pluginsDirectory = Path.GetFullPath(pluginsDirectory);
        _pendingUpgradesFilePath = Path.Combine(_pluginsDirectory, PluginPackagingConstants.PendingUpgradesFileName);
    }

    public IReadOnlyList<PendingPluginUpgrade> GetPendingUpgrades()
    {
        lock (_gate)
        {
            return ReadPendingUpgradesCore();
        }
    }

    public void AddPendingInstallOrUpgrade(string pluginId, string sourcePackagePath, string targetVersion)
    {
        AddPendingOperation(pluginId, sourcePackagePath, targetVersion, PendingPluginOperation.InstallOrUpgrade);
    }

    public void RemovePendingUpgrade(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        lock (_gate)
        {
            var upgrades = ReadPendingUpgradesCore().ToList();
            var removed = upgrades.RemoveAll(u =>
                string.Equals(u.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));

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

    public PendingPluginOperationApplySummary ApplyPendingOperations(
        PluginPackageInstaller installer,
        PluginPackageInstallOptions? options = null,
        Action<PluginPackageManifest>? prepareManifest = null)
    {
        options ??= PluginPackageInstallOptions.Default;

        lock (_gate)
        {
            var pending = ReadPendingUpgradesCore();
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

                    installer.Install(operation.SourcePackagePath, _pluginsDirectory, options, prepareManifest);
                    succeeded.Add(operation);
                }
                catch (Exception ex)
                {
                    failures.Add(new PendingPluginOperationFailure(
                        operation.PluginId,
                        operation.Operation,
                        ex.Message));
                }
            }

            var remaining = pending.Except(succeeded).ToList();
            SavePendingUpgradesCore(remaining);
            return new PendingPluginOperationApplySummary(succeeded.Count, failures.Count, failures);
        }
    }

    private void AddPendingOperation(
        string pluginId,
        string sourcePackagePath,
        string targetVersion,
        PendingPluginOperation operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVersion);

        lock (_gate)
        {
            var upgrades = ReadPendingUpgradesCore().ToList();
            upgrades.RemoveAll(u =>
                string.Equals(u.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));

            upgrades.Add(new PendingPluginUpgrade(
                pluginId,
                Path.GetFullPath(sourcePackagePath),
                targetVersion,
                DateTimeOffset.UtcNow,
                operation));

            SavePendingUpgradesCore(upgrades);
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
            var upgrades = JsonSerializer.Deserialize<List<PendingPluginUpgrade>>(json, SerializerOptions);
            return upgrades?.Where(u => u.IsValid()).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SavePendingUpgradesCore(List<PendingPluginUpgrade> upgrades)
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
