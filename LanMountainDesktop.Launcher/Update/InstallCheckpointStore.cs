using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Update;

internal sealed class InstallCheckpointStore(UpdateEnginePaths paths)
{
    public InstallCheckpoint? Load()
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

            return JsonSerializer.Deserialize(text, AppJsonContext.Default.InstallCheckpoint);
        }
        catch
        {
            return null;
        }
    }

    public void Save(InstallCheckpoint checkpoint)
    {
        File.WriteAllText(paths.InstallCheckpointPath, JsonSerializer.Serialize(checkpoint, AppJsonContext.Default.InstallCheckpoint));
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
