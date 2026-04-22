using System.Text;
using LanMountainDesktop.PluginSdk;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class PluginManifestRuntimeTests
{
    [Fact]
    public void Load_WhenRuntimeIsMissing_DefaultsToInProcess()
    {
        const string json = """
            {
              "id": "plugin.runtime.default",
              "name": "Runtime Default",
              "entranceAssembly": "Plugin.dll"
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var manifest = PluginManifest.Load(stream, "plugin.json");

        Assert.NotNull(manifest.Runtime);
        Assert.Equal(PluginRuntimeModes.InProcess, manifest.Runtime!.Mode);
        Assert.Equal(PluginRuntimeMode.InProcess, manifest.RuntimeMode);
    }

    [Fact]
    public void Load_WhenRuntimeModeIsInvalid_ThrowsHelpfulError()
    {
        const string json = """
            {
              "id": "plugin.runtime.invalid",
              "name": "Runtime Invalid",
              "entranceAssembly": "Plugin.dll",
              "runtime": {
                "mode": "shared-worker"
              }
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var ex = Assert.Throws<InvalidOperationException>(() => PluginManifest.Load(stream, "plugin.json"));

        Assert.Contains("runtime.mode", ex.Message);
        Assert.Contains("shared-worker", ex.Message);
    }
}
