using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class UpdateSnapshotStore(PlondsApplyPaths paths)
{
    public string CreateSnapshotPath(string snapshotId) => paths.GetSnapshotPath(snapshotId);

    public void Save(string path, SnapshotMetadata snapshot)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, UpdateApplyJsonContext.Default.SnapshotMetadata));
    }

    public (string Path, SnapshotMetadata Snapshot)? LoadLatest()
    {
        if (!Directory.Exists(paths.SnapshotsRoot))
        {
            return null;
        }

        var snapshotPath = Directory
            .EnumerateFiles(paths.SnapshotsRoot, "*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetCreationTimeUtc)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize(File.ReadAllText(snapshotPath), UpdateApplyJsonContext.Default.SnapshotMetadata);
        return snapshot is null ? null : (snapshotPath, snapshot);
    }
}
