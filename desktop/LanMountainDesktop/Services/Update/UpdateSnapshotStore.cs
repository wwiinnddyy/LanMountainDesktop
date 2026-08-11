using System.Text.Json;

namespace LanMountainDesktop.Services.Update;

internal sealed class UpdateSnapshotStore(PlondsApplyPaths paths)
{
    public string CreateSnapshotPath(string snapshotId) => paths.GetSnapshotPath(snapshotId);

    public void Save(string path, ApplySnapshotMetadata snapshot)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, UpdateApplyJsonContext.Default.ApplySnapshotMetadata));
    }

    public (string Path, ApplySnapshotMetadata Snapshot)? LoadLatest()
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

        var snapshot = JsonSerializer.Deserialize(File.ReadAllText(snapshotPath), UpdateApplyJsonContext.Default.ApplySnapshotMetadata);
        return snapshot is null ? null : (snapshotPath, snapshot);
    }
}
