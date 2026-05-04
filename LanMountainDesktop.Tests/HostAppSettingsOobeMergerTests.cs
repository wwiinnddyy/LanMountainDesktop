using LanMountainDesktop.Launcher.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class HostAppSettingsOobeMergerTests
{
    [Fact]
    public void MergeStartupPresentation_PreservesUnrelatedJsonKeys()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LMD.OobeMerge", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, """
        {
          "LanguageCode": "ja-JP",
          "ShowInTaskbar": false,
          "EnableFadeTransition": true,
          "EnableSlideTransition": false
        }
        """);

        try
        {
            HostAppSettingsOobeMerger.MergeStartupPresentation(
                path,
                new HostAppSettingsStartupChoices(
                    ShowInTaskbar: true,
                    EnableFadeTransition: false,
                    EnableSlideTransition: true,
                    FusedPopupExperience: true,
                    AutoStartWithWindows: true));

            var json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("ja-JP", root.GetProperty("LanguageCode").GetString());
            Assert.True(root.GetProperty("ShowInTaskbar").GetBoolean());
            Assert.False(root.GetProperty("EnableFadeTransition").GetBoolean());
            Assert.True(root.GetProperty("EnableSlideTransition").GetBoolean());
            Assert.True(root.GetProperty("EnableFusedDesktop").GetBoolean());
            Assert.True(root.GetProperty("EnableThreeFingerSwipe").GetBoolean());
            Assert.True(root.GetProperty("AutoStartWithWindows").GetBoolean());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetSettingsFilePath_NormalizesDataRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "LMD.OobePath", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = HostAppSettingsOobeMerger.GetSettingsFilePath(root + Path.DirectorySeparatorChar);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "settings.json"), path);
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    [Fact]
    public void LoadStartupDefaults_WhenFusedAndSwipeDiffer_TreatsPopupExperienceAsBothTrue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LMD.OobeDefaults", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, """
        {
          "EnableFusedDesktop": true,
          "EnableThreeFingerSwipe": false
        }
        """);

        try
        {
            var d = HostAppSettingsOobeMerger.LoadStartupDefaults(path);
            Assert.False(d.FusedPopupExperience);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
