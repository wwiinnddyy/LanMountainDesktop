using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class SystemChromeModeTests
{
    [Fact]
    public void SettingsWindow_SystemChromeUsesNativeDecorations()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "Views", "SettingsWindow.axaml.cs");
        var applyChromeMode = ExtractMethodSource(source, "ApplyChromeMode");
        var onLoaded = ExtractMethodSource(source, "OnLoaded");

        Assert.Contains("_useSystemChrome = useSystemChrome || OperatingSystem.IsMacOS();", applyChromeMode);
        Assert.Contains("WindowDecorations = WindowDecorations.Full;", applyChromeMode);
        Assert.Contains("ExtendClientAreaToDecorationsHint = !_useSystemChrome;", applyChromeMode);
        Assert.Contains("ExtendClientAreaTitleBarHeightHint = _useSystemChrome ? 0d : CustomTitleBarHeight;", applyChromeMode);
        Assert.Contains("TitleBar.ExtendsContentIntoTitleBar = !_useSystemChrome;", applyChromeMode);
        Assert.Contains("WindowTitleBarHost.IsVisible = false;", applyChromeMode);
        Assert.Contains("WindowTitleBarHost.IsVisible = true;", applyChromeMode);
        Assert.DoesNotContain("TitleBar.ExtendsContentIntoTitleBar = true;", onLoaded);
    }

    [Fact]
    public void ComponentEditorWindow_SystemChromeUsesNativeDecorations()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "Views", "ComponentEditorWindow.axaml.cs");
        var applyChromeMode = ExtractMethodSource(source, "ApplyChromeMode");

        Assert.Contains("var preferSystemChrome = useSystemChrome || OperatingSystem.IsMacOS();", applyChromeMode);
        Assert.Contains("WindowDecorations = WindowDecorations.Full;", applyChromeMode);
        Assert.Contains("ExtendClientAreaToDecorationsHint = false;", applyChromeMode);
        Assert.Contains("ExtendClientAreaTitleBarHeightHint = 0d;", applyChromeMode);
        Assert.Contains("CustomTitleBarHost.IsVisible = false;", applyChromeMode);
        Assert.Contains("WindowDecorations = WindowDecorations.BorderOnly;", applyChromeMode);
        Assert.Contains("ExtendClientAreaToDecorationsHint = true;", applyChromeMode);
        Assert.Contains("CustomTitleBarHost.IsVisible = true;", applyChromeMode);
    }

    [Fact]
    public void SavingSystemChromeSynchronizesWindowsPatcherState()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "Services", "Settings", "SettingsDomainServices.cs");

        Assert.Contains("if (OperatingSystem.IsWindows())", source);
        Assert.Contains("LanMountainDesktop.Platform.Windows.ChromePatchState.UseSystemChrome = state.UseSystemChrome;", source);
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

    private static string ExtractMethodSource(string source, string methodName)
    {
        var methodIndex = source.IndexOf($"private void {methodName}(", StringComparison.Ordinal);
        if (methodIndex < 0)
        {
            methodIndex = source.IndexOf($"public void {methodName}(", StringComparison.Ordinal);
        }

        Assert.True(methodIndex >= 0, $"Could not locate method '{methodName}'.");

        var braceIndex = source.IndexOf('{', methodIndex);
        Assert.True(braceIndex >= 0, $"Could not locate method body for '{methodName}'.");

        var depth = 0;
        for (var i = braceIndex; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(methodIndex, i - methodIndex + 1);
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method '{methodName}'.");
    }
}
