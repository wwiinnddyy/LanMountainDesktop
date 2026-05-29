using System.Text.Json;

namespace LanMountainDesktop.Services.Update;

internal sealed class ApplyInstallCheckpointStore(PlondsApplyPaths paths)
{
    public ApplyInstallCheckpoint? Load()
    {
        if (!File.Exists(paths.InstallCheckpointPath))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(paths.InstallCheckpointPath);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return JsonSerializer.Deserialize(text, UpdateApplyJsonContext.Default.ApplyInstallCheckpoint);
        }
        catch
        {
            return null;
        }
    }

    public void Save(ApplyInstallCheckpoint checkpoint)
    {
        File.WriteAllText(paths.InstallCheckpointPath, JsonSerializer.Serialize(checkpoint, UpdateApplyJsonContext.Default.ApplyInstallCheckpoint));
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(paths.InstallCheckpointPath))
            {
                File.Delete(paths.InstallCheckpointPath);
            }
        }
        catch
        {
        }
    }
}
