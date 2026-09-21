using LanMountainDesktop.Services;
using LanMountainDesktop.Shared.Contracts.Launcher;
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
    public void MergeStartupPresentation_WritesTheThemeChosenInOobe()
    {
        // 动因：向导里选了"深色"只作用在启动器自己的窗口上，宿主的 settings.json 从来没被写过
        // ThemeMode，所以首启结束后宿主仍是默认浅色 —— 用户的选择不落地。
        var (path, dir) = NewSettingsFile();
        try
        {
            HostAppSettingsOobeMerger.MergeStartupPresentation(
                path,
                new HostAppSettingsStartupChoices(
                    ShowInTaskbar: true,
                    EnableFadeTransition: true,
                    EnableSlideTransition: false,
                    FusedPopupExperience: false,
                    AutoStartWithWindows: false,
                    ThemeMode: HostAppSettingsOobeMerger.ThemeModeDarkValue));

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

            // 顺带钉住跨二进制契约：Launcher 写的字面量必须就是宿主认的那一档。
            Assert.Equal(ThemeAppearanceValues.ThemeModeDark, doc.RootElement.GetProperty(HostAppSettingsOobeMerger.ThemeModeKey).GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MergeStartupPresentation_DefaultsToLightTheme()
    {
        var (path, dir) = NewSettingsFile();
        try
        {
            HostAppSettingsOobeMerger.MergeStartupPresentation(
                path,
                new HostAppSettingsStartupChoices(
                    ShowInTaskbar: true,
                    EnableFadeTransition: true,
                    EnableSlideTransition: false,
                    FusedPopupExperience: false,
                    AutoStartWithWindows: false));

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

            Assert.Equal(ThemeAppearanceValues.ThemeModeLight, doc.RootElement.GetProperty(HostAppSettingsOobeMerger.ThemeModeKey).GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static (string SettingsPath, string Root) NewSettingsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "LMD.OobeMerge", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (Path.Combine(root, "settings.json"), root);
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

    [Theory]
    [InlineData("RestartApp", MultiInstanceLaunchBehavior.RestartApp)]
    [InlineData("OpenDesktopSilently", MultiInstanceLaunchBehavior.OpenDesktopSilently)]
    [InlineData("PromptOnly", MultiInstanceLaunchBehavior.PromptOnly)]
    [InlineData("NotifyAndOpenDesktop", MultiInstanceLaunchBehavior.NotifyAndOpenDesktop)]
    public void LoadMultiInstanceLaunchBehavior_ReadsStringValues(
        string value,
        MultiInstanceLaunchBehavior expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "LMD.MultiInstanceSettings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, $$"""
        {
          "MultiInstanceLaunchBehavior": "{{value}}"
        }
        """);

        try
        {
            Assert.Equal(expected, HostAppSettingsOobeMerger.LoadMultiInstanceLaunchBehavior(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"MultiInstanceLaunchBehavior\": \"Unknown\" }")]
    public void LoadMultiInstanceLaunchBehavior_FallsBackToNotifyAndOpenDesktop(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "LMD.MultiInstanceSettings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, json);

        try
        {
            Assert.Equal(
                MultiInstanceLaunchBehavior.NotifyAndOpenDesktop,
                HostAppSettingsOobeMerger.LoadMultiInstanceLaunchBehavior(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
