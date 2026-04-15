namespace LanMountainDesktop.Launcher.Services;

internal sealed class OobeStateService
{
    private readonly string _markerPath;

    public OobeStateService(string appRoot)
    {
        var stateDir = Path.Combine(appRoot, ".launcher", "state");
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
