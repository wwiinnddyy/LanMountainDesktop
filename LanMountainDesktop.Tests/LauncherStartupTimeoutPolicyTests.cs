using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class LauncherStartupTimeoutPolicyTests
{
    [Fact]
    public void LauncherStartupTimeouts_MatchSlowStartupContract()
    {
        var source = ReadRepositoryFile("LanMountainDesktop.Launcher", "Startup", "StartupTimeoutPolicy.cs");

        Assert.Contains("SoftTimeout = TimeSpan.FromSeconds(30)", source);
        Assert.Contains("HardTimeout = TimeSpan.FromSeconds(120)", source);
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
