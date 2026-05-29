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
            TryDeleteFile(path);
        }

        TryDeleteDirectory(paths.PlondsObjectsRoot);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }
}
