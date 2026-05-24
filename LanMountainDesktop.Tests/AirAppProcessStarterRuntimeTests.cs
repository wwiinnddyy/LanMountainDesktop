using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Launcher.Services.AirApp;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class AirAppProcessStarterRuntimeTests : IDisposable
{
    private readonly string _root;

    public AirAppProcessStarterRuntimeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.AirAppProcessStarterRuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void CreateStartInfo_UsesPackagedExecutable_WhenExeExists()
    {
        var hostPath = Path.Combine(_root, OperatingSystem.IsWindows()
            ? "LanMountainDesktop.AirAppHost.exe"
            : "LanMountainDesktop.AirAppHost");
        File.WriteAllText(hostPath, string.Empty);

        var startInfo = AirAppProcessStarter.CreateStartInfo(hostPath);

        Assert.Equal(hostPath, startInfo.FileName);
        Assert.Empty(startInfo.ArgumentList);
    }

    [Fact]
    public void CreateStartInfo_UsesArchitectureMatchedDotnetHost_ForDllFallbackOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var programFiles = Path.Combine(_root, "ProgramFiles");
        var dotnetRoot = Path.Combine(programFiles, "dotnet");
        Directory.CreateDirectory(dotnetRoot);
        var dotnetHost = Path.Combine(dotnetRoot, "dotnet.exe");
        File.WriteAllText(dotnetHost, string.Empty);
        Directory.CreateDirectory(Path.Combine(
            dotnetRoot,
            "shared",
            DotNetRuntimeProbe.RequiredSharedFrameworkName,
            "10.0.5"));

        var hostDll = Path.Combine(_root, "LanMountainDesktop.AirAppHost.dll");
        File.WriteAllText(hostDll, string.Empty);
        var options = new DotNetRuntimeProbeOptions
        {
            Architecture = DotNetRuntimeArchitecture.X64,
            ProgramFilesPath = programFiles,
            ProgramFilesX86Path = Path.Combine(_root, "ProgramFilesX86"),
            IncludeRegistry = false,
            IncludeDotNetCli = false
        };

        var startInfo = AirAppProcessStarter.CreateStartInfo(hostDll, options);

        Assert.Equal(dotnetHost, startInfo.FileName);
        Assert.Equal(hostDll, startInfo.ArgumentList.Single());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
