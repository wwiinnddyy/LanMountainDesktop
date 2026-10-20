using System.Text.Json;
using System.Text.Json.Serialization;
using LanMountainDesktop.Shared.Contracts.Update;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Shared.Contracts.Deployment;

internal static class DeploymentCommit
{
    internal static void Activate(string launcherRoot, string targetDirectory, string? rootBackupDirectory = null)
    {
        if (!PathContainment.IsSameOrChild(launcherRoot, targetDirectory) ||
            !File.Exists(Path.Combine(targetDirectory, DeploymentLayout.GetHostExecutableName())))
            throw new InvalidDataException("Cannot activate an incomplete deployment.");

        var previous = Directory.EnumerateDirectories(launcherRoot, DeploymentLayout.DeploymentDirectoryPrefix + "*")
            .Where(path => !string.Equals(path, targetDirectory, StringComparison.OrdinalIgnoreCase))
            .Where(path => File.Exists(Path.Combine(path, DeploymentLayout.CurrentMarkerFileName)))
            .ToArray();
        var snapshotPaths = new List<string>();
        try
        {
            var snapshots = UpdatePaths.GetSnapshotsDirectory(launcherRoot);
            Directory.CreateDirectory(snapshots);
            foreach (var path in previous)
            {
                var id = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}";
                var snapshotPath = Path.Combine(snapshots, id + ".json");
                var snapshot = new SnapshotMetadata
                {
                    SnapshotId = id, SourceDirectory = path, TargetDirectory = targetDirectory,
                    SourceVersion = Path.GetFileName(path).Split('-')[1],
                    TargetVersion = Path.GetFileName(targetDirectory).Split('-')[1],
                    CreatedAt = DateTimeOffset.UtcNow, Status = "installed"
                };
                File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot, DeploymentCommitJsonContext.Default.SnapshotMetadata));
                snapshotPaths.Add(snapshotPath);
                if (rootBackupDirectory is not null)
                    File.Copy(Path.Combine(rootBackupDirectory, "restore.json"), snapshotPath + ".root-backup");
            }

            // Publish the new marker before withdrawing the previous one; a crash must retain a usable app.
            File.WriteAllText(Path.Combine(targetDirectory, DeploymentLayout.CurrentMarkerFileName), string.Empty);
            var partial = Path.Combine(targetDirectory, DeploymentLayout.PartialMarkerFileName);
            if (File.Exists(partial)) File.Delete(partial);
            var destroy = Path.Combine(targetDirectory, DeploymentLayout.DestroyMarkerFileName);
            if (File.Exists(destroy)) File.Delete(destroy);
            foreach (var path in previous)
                File.Delete(Path.Combine(path, DeploymentLayout.CurrentMarkerFileName));
        }
        catch
        {
            foreach (var path in previous)
                File.WriteAllText(Path.Combine(path, DeploymentLayout.CurrentMarkerFileName), string.Empty);
            var current = Path.Combine(targetDirectory, DeploymentLayout.CurrentMarkerFileName);
            if (File.Exists(current)) File.Delete(current);
            DeploymentStaging.MarkPartial(targetDirectory);
            foreach (var path in snapshotPaths)
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".root-backup")) File.Delete(path + ".root-backup");
            }
            throw;
        }
    }
}

[JsonSerializable(typeof(SnapshotMetadata))]
internal partial class DeploymentCommitJsonContext : JsonSerializerContext;
