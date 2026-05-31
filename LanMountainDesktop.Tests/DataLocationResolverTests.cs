using LanMountainDesktop.Launcher.Models;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DataLocationResolverTests : IDisposable
{
    private readonly string _appRoot = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(DataLocationResolverTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ApplyLocationChoice_PortableWithoutCustomPath_UsesAppRootDesktopDirectory()
    {
        Directory.CreateDirectory(_appRoot);
        var resolver = new DataLocationResolver(_appRoot);

        var applied = resolver.ApplyLocationChoice(DataLocationMode.Portable);

        Assert.True(applied);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(_appRoot), "Desktop"),
            resolver.ResolveDataRoot());
    }

    public void Dispose()
    {
        if (Directory.Exists(_appRoot))
        {
            Directory.Delete(_appRoot, recursive: true);
        }
    }
}
