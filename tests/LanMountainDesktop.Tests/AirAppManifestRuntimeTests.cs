using System.Text;
using LanMountainDesktop.AirAppSdk;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class AirAppManifestRuntimeTests
{
    [Fact]
    public void Load_WhenRuntimeIsMissing_DefaultsToInProcess()
    {
        const string json = """
            {
              "id": "plugin.runtime.default",
              "name": "Runtime Default",
              "entranceAssembly": "AirApp.dll"
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var manifest = LanMountainDesktop.AirAppSdk.AirAppManifest.Load(stream, "airapp.json");

        Assert.NotNull(manifest.Runtime);
        Assert.Equal(AirAppRuntimeModes.InProcess, manifest.Runtime!.Mode);
        Assert.Equal(AirAppRuntimeMode.InProcess, manifest.RuntimeMode);
    }

    [Fact]
    public void Load_WhenRuntimeModeIsInvalid_ThrowsHelpfulError()
    {
        const string json = """
            {
              "id": "plugin.runtime.invalid",
              "name": "Runtime Invalid",
              "entranceAssembly": "AirApp.dll",
              "runtime": {
                "mode": "shared-worker"
              }
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var ex = Assert.Throws<InvalidOperationException>(() => LanMountainDesktop.AirAppSdk.AirAppManifest.Load(stream, "airapp.json"));

        Assert.Contains("runtime.mode", ex.Message);
        Assert.Contains("shared-worker", ex.Message);
    }
}
