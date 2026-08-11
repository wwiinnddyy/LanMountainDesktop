using LanMountainDesktop.Shared.Contracts.Launcher;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class StartupVisualPreferencesTests
{
    [Fact]
    public void FromFlags_WhenSlideEnabled_DisablesFadeAndUsesSlideMode()
    {
        var preferences = StartupVisualPreferencesResolver.FromFlags(
            enableFadeTransition: true,
            enableSlideTransition: true);

        Assert.False(preferences.EnableFadeTransition);
        Assert.True(preferences.EnableSlideTransition);
        Assert.Equal(StartupVisualMode.SlideSplash, preferences.Mode);
    }

    [Fact]
    public void FromFlags_WhenFadeDisabledAndSlideDisabled_UsesStaticSplashMode()
    {
        var preferences = StartupVisualPreferencesResolver.FromFlags(
            enableFadeTransition: false,
            enableSlideTransition: false);

        Assert.False(preferences.EnableFadeTransition);
        Assert.False(preferences.EnableSlideTransition);
        Assert.Equal(StartupVisualMode.StaticSplash, preferences.Mode);
    }

    [Fact]
    public void Resolve_WhenFadeSettingMissing_DefaultsToFadeEnabled()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.StartupVisualTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var settingsPath = Path.Combine(tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, """
        {
          "enableSlideTransition": false
        }
        """);

        try
        {
            var preferences = StartupVisualPreferencesResolver.Resolve(settingsPath);

            Assert.True(preferences.EnableFadeTransition);
            Assert.False(preferences.EnableSlideTransition);
            Assert.Equal(StartupVisualMode.Fade, preferences.Mode);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
