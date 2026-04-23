using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Shared.Contracts.Launcher;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class HostLaunchPlanBuilderTests : IDisposable
{
    private readonly string _testRoot;

    public HostLaunchPlanBuilderTests()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "LanMountainDesktop.HostLaunchPlanTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public void Build_UsesPackageRootAsWorkingDirectory_ForPublishedDeployment()
    {
        var packageRoot = Path.Combine(_testRoot, "package-root");
        var deployment = CreateDeployment(packageRoot, "app-0.8.5.7");
        var resultPath = Path.Combine(_testRoot, "launcher-result.json");
        var context = CommandContext.FromArgs(
        [
            "launch",
            "--app-root", packageRoot,
            "--result", resultPath,
            "--launch-source", "postinstall",
            "--custom-host-arg", "custom-value"
        ]);
        var locator = new DeploymentLocator(packageRoot);
        var resolution = locator.ResolveHostExecutable(context);

        var plan = HostLaunchPlanBuilder.Build(context, locator, resolution);

        Assert.Equal(Path.GetFullPath(packageRoot), plan.PackageRoot);
        Assert.Equal(Path.GetFullPath(packageRoot), plan.WorkingDirectory);
        Assert.Equal(Path.Combine(deployment, GetExecutableName()), plan.HostPath);
        Assert.Contains("--launch-source", plan.Arguments);
        Assert.Contains("postinstall", plan.Arguments);
        Assert.Contains("--custom-host-arg", plan.Arguments);
        Assert.Contains("custom-value", plan.Arguments);
        Assert.DoesNotContain("--app-root", plan.Arguments);
        Assert.DoesNotContain(packageRoot, plan.Arguments);
        Assert.DoesNotContain("--result", plan.Arguments);
        Assert.DoesNotContain(resultPath, plan.Arguments);
        Assert.Contains($"--{LauncherIpcConstants.PackageRootEnvVar}={Path.GetFullPath(packageRoot)}", plan.Arguments);
    }

    [Fact]
    public void Build_KeepsPathsWithSpacesAsSingleArgumentListTokens()
    {
        var packageRoot = Path.Combine(_testRoot, "package root with spaces");
        CreateDeployment(packageRoot, "app-0.8.5.7");
        var context = CommandContext.FromArgs(["launch", "--app-root", packageRoot]);
        var locator = new DeploymentLocator(packageRoot);
        var resolution = locator.ResolveHostExecutable(context);

        var plan = HostLaunchPlanBuilder.Build(context, locator, resolution);

        var packageRootArgument = $"--{LauncherIpcConstants.PackageRootEnvVar}={Path.GetFullPath(packageRoot)}";
        Assert.Contains(packageRootArgument, plan.Arguments);
        Assert.Equal(Path.GetFullPath(packageRoot), plan.EnvironmentVariables[LauncherIpcConstants.PackageRootEnvVar]);
        Assert.DoesNotContain(plan.Arguments, argument => argument.StartsWith("\"", StringComparison.Ordinal));
        Assert.Equal(Path.GetFullPath(packageRoot), plan.WorkingDirectory);
    }

    private static string CreateDeployment(string packageRoot, string deploymentName)
    {
        var deployment = Path.Combine(packageRoot, deploymentName);
        Directory.CreateDirectory(deployment);
        File.WriteAllText(Path.Combine(deployment, GetExecutableName()), string.Empty);
        File.WriteAllText(Path.Combine(deployment, ".current"), string.Empty);
        File.WriteAllText(
            Path.Combine(deployment, "version.json"),
            """
            {"Version":"0.8.5.7","Codename":"Administrate"}
            """);
        return deployment;
    }

    private static string GetExecutableName()
    {
        return OperatingSystem.IsWindows()
            ? "LanMountainDesktop.exe"
            : "LanMountainDesktop";
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }
}
