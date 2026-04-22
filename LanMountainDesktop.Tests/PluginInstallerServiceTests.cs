using LanMountainDesktop.Launcher.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class PluginInstallerServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.Tests", nameof(PluginInstallerServiceTests), Guid.NewGuid().ToString("N"));

    [Fact]
    public void InstallPackage_ReturnsElevationRequired_ForOutsideUserScope_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_tempRoot);
        var packagePath = Path.Combine(_tempRoot, "sample.lmdp");
        File.WriteAllText(packagePath, "placeholder");

        var service = new PluginInstallerService();
        var result = service.InstallPackage(packagePath, Path.Combine(_tempRoot, "Plugins"));

        Assert.False(result.Success);
        Assert.Equal("plugin_elevation_required", result.Code);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
        }
    }
}
