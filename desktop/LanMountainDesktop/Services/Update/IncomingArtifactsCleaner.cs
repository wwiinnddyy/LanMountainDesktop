using LanMountainDesktop.Shared.IO;
namespace LanMountainDesktop.Services.Update;

internal sealed class IncomingArtifactsCleaner(PlondsApplyPaths paths)
{
    public void Cleanup()
    {
        foreach (var path in new[]
                 {
                     paths.FileMapPath,
                     paths.SignaturePath,
                     paths.ArchivePath,
                     paths.PlondsFileMapPath,
                     paths.PlondsSignaturePath,
                     paths.PlondsUpdateMetadataPath,
                     paths.InstallCheckpointPath,
                     paths.DownloadMarkerPath
                 })
        {
            FileOperationRetryHelper.TryDeleteFile(path, "Update");
        }

        FileOperationRetryHelper.TryDeleteDirectory(paths.PlondsObjectsRoot, true, "Update");
    }
}
