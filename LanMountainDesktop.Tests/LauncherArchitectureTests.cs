using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class LauncherArchitectureTests
{
    [Fact]
    public void CoreLauncherFolders_DoNotUseAvaloniaNamespaces()
    {
        var forbidden = new[] { "Deployment", "Startup", "Infrastructure" };
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
            Path.Combine(LauncherProjectRoot, "Infrastructure", "Commands.cs")
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
    public void LauncherProject_DoesNotOwnUpdateApplyOrRollback()
    {
        var launcherFiles = Directory
            .EnumerateFiles(LauncherProjectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var forbiddenTokens = new[]
        {
            "LauncherUpdateCommandExecutor",
            "PlondsUpdateApplier",
            "UpdateRollbackGateway",
            "UpdateInstallGateway",
            "LanMountainDesktop.Services.Update",
            "apply-update",
            "rollback --app-root"
        };

        var offenders = launcherFiles
            .SelectMany(file => forbiddenTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{RelativeToRepo(file)} contains {token}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void LauncherProjectFile_DoesNotSourceLinkHostUpdateImplementation()
    {
        var project = File.ReadAllText(Path.Combine(LauncherProjectRoot, "LanMountainDesktop.Launcher.csproj"));

        Assert.DoesNotContain(@"..\LanMountainDesktop\Services\Update", project, StringComparison.Ordinal);
        Assert.DoesNotContain("PlondsUpdateApplier", project, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateRollbackGateway", project, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateInstallGateway", project, StringComparison.Ordinal);
    }

    [Fact]
    public void HostUpdateFlow_DoesNotDelegateApplyOrRollbackToLauncher()
    {
        var guardedFiles = new[]
        {
            Path.Combine(RepoRoot, "LanMountainDesktop", "Services", "Update", "UpdateInstallGateway.cs"),
            Path.Combine(RepoRoot, "LanMountainDesktop", "Services", "Update", "UpdateOrchestrator.cs")
        };

        var forbiddenTokens = new[]
        {
            "LauncherPathResolver",
            "ResolveLauncherExecutablePath",
            "apply-update",
            "rollback --app-root",
            "Launched Launcher"
        };

        var offenders = guardedFiles
            .SelectMany(file => forbiddenTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{RelativeToRepo(file)} contains {token}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void HostUpdateFlow_OwnsDeltaApplyAndRollbackExecution()
    {
        var installGateway = File.ReadAllText(Path.Combine(
            RepoRoot,
            "LanMountainDesktop",
            "Services",
            "Update",
            "UpdateInstallGateway.cs"));
        var orchestrator = File.ReadAllText(Path.Combine(
            RepoRoot,
            "LanMountainDesktop",
            "Services",
            "Update",
            "UpdateOrchestrator.cs"));

        Assert.Contains("new PlondsUpdateApplier", installGateway, StringComparison.Ordinal);
        Assert.Contains("DeploymentLockService.ClearLock", installGateway, StringComparison.Ordinal);
        Assert.Contains("new UpdateRollbackGateway().RollbackLatest", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("LanMountainDesktop.Launcher", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void LauncherCompositionRootStaysThin()
    {
        AssertFileLineCountAtMost(Path.Combine(LauncherProjectRoot, "Shell", "LauncherCompositionRoot.cs"), 80);
    }

    [Fact]
    public void SuccessfulLauncherHandoff_DoesNotWaitForHostProcessExit()
    {
        var coordinator = File.ReadAllText(Path.Combine(
            LauncherProjectRoot,
            "Shell",
            "LauncherGuiCoordinator.cs"));

        Assert.Contains("AttachHostAsync(hostPid)", coordinator, StringComparison.Ordinal);
        Assert.Contains("desktop.Shutdown(Environment.ExitCode)", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitForHostProcessToExit", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("Launcher entering host background lifetime", coordinator, StringComparison.Ordinal);
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
