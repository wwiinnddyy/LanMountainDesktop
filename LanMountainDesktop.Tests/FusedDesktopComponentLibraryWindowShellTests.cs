using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class FusedDesktopComponentLibraryWindowShellTests
{
    [Fact]
    public void Window_UsesTransparentFullClientArea()
    {
        var xaml = ReadRepositoryFile(
            "LanMountainDesktop",
            "Views",
            "FusedDesktopComponentLibraryWindow.axaml");
        var window = ExtractElementStart(xaml, "<Window ");

        Assert.Contains("WindowDecorations=\"None\"", window);
        Assert.Contains("Background=\"Transparent\"", window);
        Assert.Contains("TransparencyLevelHint=\"Transparent\"", window);
        Assert.Contains("ExtendClientAreaToDecorationsHint=\"True\"", window);
        Assert.Contains("ExtendClientAreaTitleBarHeightHint=\"-1\"", window);
    }

    [Fact]
    public void PanelShell_FillsClientAreaWithoutOuterShadowGutter()
    {
        var xaml = ReadRepositoryFile(
            "LanMountainDesktop",
            "Views",
            "FusedDesktopComponentLibraryWindow.axaml");
        var panelShell = ExtractElementStart(xaml, "<Border x:Name=\"PanelShell\"");

        Assert.Contains("Classes=\"surface-translucent-strong\"", panelShell);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", panelShell);
        Assert.Contains("VerticalAlignment=\"Stretch\"", panelShell);
        Assert.Contains("Margin=\"0\"", panelShell);
        Assert.Contains("BoxShadow=\"none\"", panelShell);
        Assert.Contains("CornerRadius=\"{DynamicResource DesignCornerRadiusLg}\"", panelShell);
        Assert.Contains("ClipToBounds=\"True\"", panelShell);
        Assert.DoesNotContain("Margin=\"10\"", panelShell);
    }

    [Fact]
    public void Window_DisablesNativeWindowsCornerAndBorderTreatment()
    {
        var codeBehind = ReadRepositoryFile(
            "LanMountainDesktop",
            "Views",
            "FusedDesktopComponentLibraryWindow.axaml.cs");

        Assert.Contains("Win32Properties.SetWindowCornerPreference(", codeBehind);
        Assert.Contains("Win32Properties.WindowCornerPreference.DoNotRound", codeBehind);
        Assert.Contains("DwmWindowAttributeBorderColor = 34", codeBehind);
        Assert.Contains("DwmColorNone = 0xFFFFFFFE", codeBehind);
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
