using LanMountainDesktop.Launcher.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

[Collection("LauncherDebugSettingsStore")]
public sealed class LauncherDebugSettingsStoreTests : IDisposable
{
    private readonly string _tempDirectory;

    public LauncherDebugSettingsStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.DebugSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        LauncherDebugSettingsStore.ConfigBaseDirectoryOverride = _tempDirectory;
    }

    [Fact]
    public void Load_WhenOnlyLegacyFilesExist_ReadsLegacySettings()
    {
        var customPath = Path.Combine(_tempDirectory, "legacy-host.exe");
        File.WriteAllText(Path.Combine(_tempDirectory, "devmode.config"), "1");
        File.WriteAllText(Path.Combine(_tempDirectory, "custom-host-path.config"), customPath);

        var settings = LauncherDebugSettingsStore.Load();

        Assert.True(settings.DevModeEnabled);
        Assert.Equal(customPath, settings.CustomHostPath);
    }

    [Fact]
    public void Save_WritesNewSettingsFiles()
    {
        var customPath = Path.Combine(_tempDirectory, "host.exe");

        LauncherDebugSettingsStore.Save(new LauncherDebugSettings(true, customPath));

        Assert.Equal("True", File.ReadAllText(Path.Combine(_tempDirectory, "dev-mode.flag")).Trim());
        Assert.Equal(customPath, File.ReadAllText(Path.Combine(_tempDirectory, "custom-host-path.txt")).Trim());
    }

    public void Dispose()
    {
        LauncherDebugSettingsStore.ConfigBaseDirectoryOverride = null;
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
