using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class AirAppRuntimeDataPathTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(AirAppRuntimeDataPathTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void AirAppRuntime_UsesHostDataRootForAirAppsAndMarketData()
    {
        AppDataPathProvider.Initialize(["--data-root", _dataRoot]);

        using var runtime = new AirAppRuntimeService();

        Assert.Equal(
            Path.Combine(Path.GetFullPath(_dataRoot), "Extensions", "AirApps"),
            runtime.AirAppsDirectory);
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
