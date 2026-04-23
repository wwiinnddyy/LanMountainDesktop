using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

[Collection("LauncherDebugSettingsStore")]
public sealed class DeploymentLocatorTests : IDisposable
{
    private readonly string _appRoot;
    private readonly string _configRoot;

    public DeploymentLocatorTests()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.DeploymentLocatorTests", Guid.NewGuid().ToString("N"));
        _appRoot = Path.Combine(testRoot, "app-root");
        _configRoot = Path.Combine(testRoot, "config");
        Directory.CreateDirectory(_appRoot);
        Directory.CreateDirectory(_configRoot);
        LauncherDebugSettingsStore.ConfigBaseDirectoryOverride = _configRoot;
    }

    [Fact]
    public void ResolveHostExecutable_WhenSavedDebugPathIsMalformed_DoesNotThrow()
    {
        LauncherDebugSettingsStore.Save(new LauncherDebugSettings(true, "bad\0path"));

        var locator = new DeploymentLocator(_appRoot);
        var result = locator.ResolveHostExecutable(CommandContext.FromArgs(["launch", "--debug"]));

        Assert.NotEqual("debug_saved_custom_path", result.ResolutionSource);
    }

    public void Dispose()
    {
        LauncherDebugSettingsStore.ConfigBaseDirectoryOverride = null;
        var testRoot = Directory.GetParent(_appRoot)?.FullName;
        if (!string.IsNullOrWhiteSpace(testRoot) && Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
}
