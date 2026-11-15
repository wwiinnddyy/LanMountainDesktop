using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class SettingsWindowShellVisualTests
{
    [Fact]
    public void SettingsWindow_UsesOneFullWindowBackgroundBehindTitlebarAndContent()
    {
        var xaml = ReadRepositoryFile("desktop", "LanMountainDesktop", "Views", "SettingsWindow.axaml");

        Assert.Contains("x:Name=\"RootGrid\"", xaml);
        Assert.Contains("Background=\"Transparent\"", ExtractElementStart(xaml, "<Grid x:Name=\"RootGrid\""));
        Assert.Contains("Grid.RowSpan=\"2\"", xaml);
        Assert.Contains("Background=\"{DynamicResource AdaptiveSettingsWindowBackgroundBrush}\"", xaml);
        Assert.Contains("Background=\"{DynamicResource AdaptiveSettingsWindowTintBrush}\"", xaml);
    }

    [Fact]
    public void SettingsWindow_TitlebarDoesNotPaintASeparateSurfaceBand()
    {
        var xaml = ReadRepositoryFile("desktop", "LanMountainDesktop", "Views", "SettingsWindow.axaml");
        var titlebar = ExtractElementStart(xaml, "<Border x:Name=\"WindowTitleBarHost\"");

        Assert.Contains("Background=\"Transparent\"", titlebar);
        Assert.Contains("BorderBrush=\"Transparent\"", titlebar);
        Assert.Contains("BorderThickness=\"0\"", titlebar);
        Assert.DoesNotContain("BorderThickness=\"0,0,0,1\"", titlebar);
        Assert.DoesNotContain("AdaptiveSettingsWindowBackgroundBrush", titlebar);
    }

    [Fact]
    public void SettingsWindow_NavigationShellBackgroundsAreTransparent()
    {
        // Phase 2.1（docs/ARC-MIGRATION-PLAN.md）：导航壳换成 Arc Sidebar；
        // 壳背景保持透明，壁纸自适应背景层（RootGrid 上那两层）继续从底下透出。
        var xaml = ReadRepositoryFile("desktop", "LanMountainDesktop", "Views", "SettingsWindow.axaml");

        Assert.Contains("<arc:Sidebar x:Name=\"NavSidebar\"", xaml);
        Assert.Contains("Background=\"Transparent\"", ExtractElementStart(xaml, "<arc:Sidebar x:Name=\"NavSidebar\""));
        Assert.DoesNotContain("FANavigationView", xaml);
    }

    // NavigationStyles_KeepSettingsNavigationTemplateTransparent 已随 Phase 2.1 移除：
    // FANavigationView 换成 Arc Sidebar 后，NavigationStyles.axaml（模板透明化样式）整体退役，
    // Sidebar 的壳透明由其自身 Background="Transparent" 承担（见上面的 Sidebar 断言）。

    private static string ExtractElementStart(string source, string startToken)
    {
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find '{startToken}'.");

        var end = source.IndexOf('>', start);
        Assert.True(end > start, $"Could not find end of '{startToken}'.");

        return source.Substring(start, end - start + 1);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            if (File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                break;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }
}
