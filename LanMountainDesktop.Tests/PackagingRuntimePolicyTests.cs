using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class PackagingRuntimePolicyTests
{
    [Fact]
    public void WindowsPackageScript_PublishesLauncherRootAndFrameworkDependentAppDirectory()
    {
        var script = ReadRepositoryFile("LanMountainDesktop", "scripts", "package.ps1");

        Assert.Contains("Publish-LauncherPayload", script);
        Assert.Contains("\"app-$Version\"", script);
        Assert.Contains("Publish-MainAppFrameworkDependentPayload", script);
        Assert.Contains("\"--self-contained\", \"false\"", script);
        Assert.Contains("\"-p:SelfContained=false\"", script);
    }

    [Fact]
    public void WindowsPayloadGuard_BlocksBundledDotNetRuntimeFiles()
    {
        var script = ReadRepositoryFile("LanMountainDesktop", "scripts", "Optimize-PublishPayload.ps1");

        Assert.Contains("coreclr.dll", script);
        Assert.Contains("hostfxr.dll", script);
        Assert.Contains("hostpolicy.dll", script);
        Assert.Contains("System.Private.CoreLib.dll", script);
    }

    [Fact]
    public void WindowsPayloadGuard_RequiresLauncherMainAndAirAppHost()
    {
        var script = ReadRepositoryFile("LanMountainDesktop", "scripts", "Optimize-PublishPayload.ps1");

        Assert.Contains("Assert-WindowsPayloadContainsRequiredHosts", script);
        Assert.Contains("LanMountainDesktop.Launcher.exe", script);
        Assert.Contains("LanMountainDesktop.exe", script);
        Assert.Contains("LanMountainDesktop.AirAppHost.exe", script);
    }

    [Fact]
    public void ReleaseWorkflow_VerifiesAirAppHostBeforePublishingInstaller()
    {
        var workflow = ReadRepositoryFile(".github", "workflows", "release.yml");

        Assert.Contains("Verify Windows app host payload", workflow);
        Assert.Contains("LanMountainDesktop.AirAppHost.exe", workflow);
    }

    [Fact]
    public void Installer_DownloadsArchitectureSpecificDesktopRuntime()
    {
        var installer = ReadRepositoryFile("LanMountainDesktop", "installer", "LanMountainDesktop.iss");

        Assert.Contains("https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe", installer);
        Assert.Contains("https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x86.exe", installer);
        Assert.Contains("/install /quiet /norestart", installer);
        Assert.Contains("ExitCode <> 3010", installer);
        Assert.DoesNotContain("IsSelfContainedBuild", installer);
    }

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Unable to locate repository root.");
        }

        return File.ReadAllText(Path.Combine([directory.FullName, .. pathParts]));
    }
}
