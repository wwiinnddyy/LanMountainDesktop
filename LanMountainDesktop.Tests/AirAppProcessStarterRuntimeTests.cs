using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class AirAppProcessStarterRuntimeTests : IDisposable
{
    private readonly string _root;

    public AirAppProcessStarterRuntimeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.AirAppProcessStarterRuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void CreateStartInfo_UsesPackagedExecutable_WhenExeExists()
    {
        var hostPath = Path.Combine(_root, OperatingSystem.IsWindows()
            ? "LanMountainDesktop.AirAppHost.exe"
            : "LanMountainDesktop.AirAppHost");
        File.WriteAllText(hostPath, string.Empty);

        var startInfo = AirAppProcessStarter.CreateStartInfo(hostPath);

        Assert.Equal(hostPath, startInfo.FileName);
        Assert.Empty(startInfo.ArgumentList);
    }

    [Fact]
    public void CreateStartInfo_UsesDotnetHost_ForDllFallback()
    {
        var hostDll = Path.Combine(_root, "LanMountainDesktop.AirAppHost.dll");
        File.WriteAllText(hostDll, string.Empty);

        var startInfo = AirAppProcessStarter.CreateStartInfo(hostDll);

        Assert.Contains("dotnet", Path.GetFileName(startInfo.FileName), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(hostDll, startInfo.ArgumentList.Single());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
