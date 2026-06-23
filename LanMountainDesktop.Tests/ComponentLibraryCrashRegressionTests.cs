using System.Text.RegularExpressions;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class ComponentLibraryCrashRegressionTests
{
    [Fact]
    public void BuiltInComponentXaml_DoesNotAnimateRenderTransformDirectly()
    {
        var componentsDirectory = FindRepositoryPath("LanMountainDesktop", "Views", "Components");
        foreach (var file in Directory.EnumerateFiles(componentsDirectory, "*.axaml", SearchOption.AllDirectories))
        {
            var xaml = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(
                         xaml,
                         @"<Style\.Animations>[\s\S]*?</Style\.Animations>",
                         RegexOptions.CultureInvariant))
            {
                Assert.DoesNotContain(
                    "Property=\"RenderTransform\"",
                    match.Value,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ComponentLibraryPreviewPages_FallBackWhenPreviewAttachFails()
    {
        var source = ReadRepositoryFile("LanMountainDesktop", "Views", "MainWindow.ComponentSystem.cs");
        var buildSource = ExtractMethodSource(source, "BuildComponentLibraryComponentPages");
        var pageSource = ExtractMethodSource(source, "CreateComponentLibraryComponentPage");
        var contentSource = ExtractMethodSource(source, "CreateComponentLibraryComponentPageContent");

        Assert.Contains("try", buildSource);
        Assert.Contains("ComponentLibraryComponentPagesContainer.Children.Add(page)", buildSource);
        Assert.Contains("forceFallback: true", buildSource);
        Assert.Contains("UiExceptionGuard.IsFatalException", buildSource);

        Assert.Contains("CreateStaticComponentPreviewFallback", contentSource);
        Assert.Contains("forceFallback", pageSource);
    }

    [Fact]
    public void SoftwareRenderRetry_IsDisabledAfterAvaloniaLifetimeStarts()
    {
        var shouldRetry = Program.ShouldRetryWithSoftwareRendering(
            AppRenderingModeHelper.Default,
            new InvalidOperationException("render failed"),
            isAvaloniaLifetimeStarted: true);

        Assert.False(shouldRetry);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var path = FindRepositoryPath(segments);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            if (File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                break;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository path '{Path.Combine(segments)}'.");
    }

    private static string ExtractMethodSource(string source, string methodName)
    {
        var methodIndex = source.IndexOf($"private void {methodName}(", StringComparison.Ordinal);
        if (methodIndex < 0)
        {
            methodIndex = source.IndexOf($"private Grid {methodName}(", StringComparison.Ordinal);
        }

        if (methodIndex < 0)
        {
            methodIndex = source.IndexOf($"private StackPanel {methodName}(", StringComparison.Ordinal);
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
