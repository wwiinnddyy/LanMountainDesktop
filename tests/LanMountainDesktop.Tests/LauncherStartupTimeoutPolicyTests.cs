using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class LauncherStartupTimeoutPolicyTests
{
    [Fact]
    public void LauncherStartupTimeouts_MatchSlowStartupContract()
    {
        var source = ReadRepositoryFile("desktop", "LanMountainDesktop.Launcher", "Startup", "StartupTimeoutPolicy.cs");

        // Slow-device contract: AOT cold start may take >30s, launcher must tolerate at least 45s soft / 180s hard
        // to avoid false failure when AirAppRuntime pre-start + AirApp discovery runs on first launch.
        Assert.Contains("SoftTimeout = TimeSpan.FromSeconds(45)", source);
        Assert.Contains("HardTimeout = TimeSpan.FromSeconds(180)", source);
    }

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Unable to locate repository root.");
        }

        return File.ReadAllText(Path.Combine([directory.FullName, .. pathParts]));
    }
}
