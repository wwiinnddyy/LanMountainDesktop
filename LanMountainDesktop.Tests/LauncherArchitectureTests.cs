using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class LauncherArchitectureTests
{
    [Fact]
    public void CoreLauncherFolders_DoNotUseAvaloniaNamespaces()
    {
        var forbidden = new[] { "Deployment", "Update", "Startup", "Infrastructure" };
        foreach (var folder in forbidden.Select(folder => Path.Combine(LauncherProjectRoot, folder)))
        {
            var offenders = Directory
                .EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Where(file => File.ReadAllText(file).Contains("using Avalonia", StringComparison.Ordinal))
                .Select(RelativeToRepo)
                .ToArray();

            Assert.Empty(offenders);
        }
    }

    [Fact]
    public void LauncherFlowCoordinator_TypeDoesNotExist()
    {
        var coordinator = typeof(LanMountainDesktop.Launcher.Shell.LauncherOrchestrator).Assembly
            .GetType("LanMountainDesktop.Launcher.Services.LauncherFlowCoordinator", throwOnError: false);
        Assert.Null(coordinator);
    }

    [Fact]
    public void CliAndShellEntryHandlers_DoNotDependOnConcreteUpdateEngineFacade()
    {
        var guardedFiles = new[]
        {
            Path.Combine(LauncherProjectRoot, "Infrastructure", "Commands.cs"),
            Path.Combine(LauncherProjectRoot, "Shell", "ApplyUpdateGuiFlow.cs")
        }.Concat(Directory.EnumerateFiles(
            Path.Combine(LauncherProjectRoot, "Shell", "EntryHandlers"),
            "*.cs",
            SearchOption.AllDirectories));

        var offenders = guardedFiles
            .Where(file => File.ReadAllText(file).Contains("UpdateEngineFacade", StringComparison.Ordinal))
            .Select(RelativeToRepo)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void LauncherFacadeAndCompositionRootStayThin()
    {
        AssertFileLineCountAtMost(Path.Combine(LauncherProjectRoot, "Update", "UpdateEngineFacade.cs"), 140);
        AssertFileLineCountAtMost(Path.Combine(LauncherProjectRoot, "Shell", "LauncherCompositionRoot.cs"), 80);
    }

    private static string LauncherProjectRoot => Path.Combine(RepoRoot, "LanMountainDesktop.Launcher");

    private static string RepoRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "LanMountainDesktop.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Unable to locate repository root.");
        }
    }

    private static void AssertFileLineCountAtMost(string path, int maxLines)
    {
        var lineCount = File.ReadLines(path).Count();
        Assert.True(lineCount <= maxLines, $"{RelativeToRepo(path)} has {lineCount} lines; expected <= {maxLines}.");
    }

    private static string RelativeToRepo(string path) => Path.GetRelativePath(RepoRoot, path);
}
