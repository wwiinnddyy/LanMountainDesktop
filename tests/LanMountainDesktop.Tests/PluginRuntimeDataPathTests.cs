using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class PluginRuntimeDataPathTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(PluginRuntimeDataPathTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void PluginRuntime_UsesHostDataRootForPluginsAndMarketData()
    {
        AppDataPathProvider.Initialize(["--data-root", _dataRoot]);

        using var runtime = new PluginRuntimeService();

        Assert.Equal(
            Path.Combine(Path.GetFullPath(_dataRoot), "Extensions", "Plugins"),
            runtime.PluginsDirectory);
    }

    public void Dispose()
    {
        AppDataPathProvider.ResetForTests();
        try
        {
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }
        catch
        {
        }
    }
}
