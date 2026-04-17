namespace LanMountainDesktop.Launcher.Services;

internal sealed class OobeStateService
{
    private readonly string _markerPath;

    public OobeStateService(string appRoot)
    {
        // 将 OOBE 状态文件存储在用户可写的 LocalApplicationData 目录中，
        // 而不是安装目录（Program Files 下普通用户没有写入权限）。
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop");
        var stateDir = Path.Combine(appDataDir, ".launcher", "state");
        Directory.CreateDirectory(stateDir);
        _markerPath = Path.Combine(stateDir, "first_run_completed");
    }

    public bool IsFirstRun()
    {
        return !File.Exists(_markerPath);
    }

    public void MarkCompleted()
    {
        var dir = Path.GetDirectoryName(_markerPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_markerPath, DateTimeOffset.UtcNow.ToString("O"));
    }
}
