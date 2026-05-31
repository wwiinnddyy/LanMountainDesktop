using System.Text.Json;
using LanMountainDesktop.Shared.IPC;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class AirAppRuntimeDataRootResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.AirAppRuntimeDataRootResolverTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveDataRoot_UsesPortableDataLocationConfig()
    {
        var portableRoot = Path.Combine(_root, "PortableData");
        WriteConfig(new
        {
            dataLocationMode = "Portable",
            portableDataPath = portableRoot
        });

        var resolved = AirAppRuntimeDataRootResolver.ResolveDataRoot(_root);

        Assert.Equal(Path.GetFullPath(portableRoot), resolved);
    }

    [Fact]
    public void ResolveDataRoot_UsesSystemDataLocationConfig()
    {
        var systemRoot = Path.Combine(_root, "SystemData");
        WriteConfig(new
        {
            dataLocationMode = "System",
            systemDataPath = systemRoot
        });

        var resolved = AirAppRuntimeDataRootResolver.ResolveDataRoot(_root);

        Assert.Equal(Path.GetFullPath(systemRoot), resolved);
    }

    [Fact]
    public void ResolveDataRoot_FallsBackToDefaultWhenConfigMissing()
    {
        var resolved = AirAppRuntimeDataRootResolver.ResolveDataRoot(_root);

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanMountainDesktop"),
            resolved);
    }

    private void WriteConfig<T>(T config)
    {
        var configDirectory = Path.Combine(_root, ".Launcher");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(
            Path.Combine(configDirectory, "data-location.config.json"),
            JsonSerializer.Serialize(config));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
