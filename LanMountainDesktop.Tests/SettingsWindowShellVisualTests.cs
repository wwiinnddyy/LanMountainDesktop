using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class SettingsWindowShellVisualTests
{
    [Fact]
    public void SettingsWindow_UsesOneFullWindowBackgroundBehindTitlebarAndContent()
    {
        var xaml = ReadRepositoryFile("LanMountainDesktop", "Views", "SettingsWindow.axaml");

        Assert.Contains("x:Name=\"RootGrid\"", xaml);
        Assert.Contains("Background=\"Transparent\"", ExtractElementStart(xaml, "<Grid x:Name=\"RootGrid\""));
        Assert.Contains("Grid.RowSpan=\"2\"", xaml);
        Assert.Contains("Background=\"{DynamicResource AdaptiveSettingsWindowBackgroundBrush}\"", xaml);
        Assert.Contains("Background=\"{DynamicResource AdaptiveSettingsWindowTintBrush}\"", xaml);
    }

    [Fact]
    public void SettingsWindow_TitlebarDoesNotPaintASeparateSurfaceBand()
    {
        var xaml = ReadRepositoryFile("LanMountainDesktop", "Views", "SettingsWindow.axaml");
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
        var xaml = ReadRepositoryFile("LanMountainDesktop", "Views", "SettingsWindow.axaml");

        Assert.Contains("Classes=\"settings-navigation-view\"", xaml);
        Assert.Contains("<SolidColorBrush x:Key=\"NavigationViewContentBackground\" Color=\"Transparent\" />", xaml);
        Assert.Contains("<SolidColorBrush x:Key=\"NavigationViewContentGridBorderBrush\" Color=\"Transparent\" />", xaml);
        Assert.Contains("<SolidColorBrush x:Key=\"NavigationViewDefaultPaneBackground\" Color=\"Transparent\" />", xaml);
        Assert.Contains("<SolidColorBrush x:Key=\"NavigationViewExpandedPaneBackground\" Color=\"Transparent\" />", xaml);
        Assert.Contains("<SolidColorBrush x:Key=\"NavigationViewTopPaneBackground\" Color=\"Transparent\" />", xaml);
    }

    [Fact]
    public void NavigationStyles_KeepSettingsNavigationTemplateTransparent()
    {
        var styles = ReadRepositoryFile("LanMountainDesktop", "Styles", "NavigationStyles.axaml");

        Assert.Contains("ui|FANavigationView.settings-navigation-view", styles);
        Assert.Contains("Grid#RootGrid", styles);
        Assert.Contains("Grid#ContentGrid", styles);
        Assert.Contains("Grid#PaneRoot", styles);
        Assert.Contains("Border#NavigationViewBorder", styles);
        Assert.Contains("Border#ContentGridBorder", styles);
        Assert.Contains("Border#PaneBorder", styles);
        Assert.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", styles);
        Assert.Contains("<Setter Property=\"BorderBrush\" Value=\"Transparent\" />", styles);
    }

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
