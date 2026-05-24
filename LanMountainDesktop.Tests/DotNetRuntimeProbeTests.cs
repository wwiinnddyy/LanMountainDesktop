using LanMountainDesktop.Launcher.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DotNetRuntimeProbeTests : IDisposable
{
    private readonly string _root;
    private readonly string _programFiles;
    private readonly string _programFilesX86;
    private readonly string _localAppData;

    public DotNetRuntimeProbeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.DotNetRuntimeProbeTests", Guid.NewGuid().ToString("N"));
        _programFiles = Path.Combine(_root, "ProgramFiles");
        _programFilesX86 = Path.Combine(_root, "ProgramFilesX86");
        _localAppData = Path.Combine(_root, "LocalAppData");
        Directory.CreateDirectory(_programFiles);
        Directory.CreateDirectory(_programFilesX86);
        Directory.CreateDirectory(_localAppData);
    }

    [Fact]
    public void Probe_AcceptsTargetArchitectureRuntime_WhenDotnetHostIsMissing()
    {
        CreateRuntime(_programFiles, "10.0.5");

        var result = DotNetRuntimeProbe.Probe(CreateOptions(DotNetRuntimeArchitecture.X64));

        Assert.True(result.IsAvailable);
        Assert.Null(result.DotNetHostPath);
        Assert.Contains(result.DetectedRuntimes, runtime => runtime.Version == "10.0.5");
    }

    [Fact]
    public void Probe_X64DoesNotAcceptX86OnlyRuntime()
    {
        CreateRuntime(_programFilesX86, "10.0.5");

        var result = DotNetRuntimeProbe.Probe(CreateOptions(DotNetRuntimeArchitecture.X64));

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void Probe_X86DoesNotAcceptX64OnlyRuntime()
    {
        CreateRuntime(_programFiles, "10.0.5");

        var result = DotNetRuntimeProbe.Probe(CreateOptions(DotNetRuntimeArchitecture.X86));

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void Probe_RejectsOlderMajorVersions()
    {
        CreateRuntime(_programFiles, "8.0.25");
        CreateRuntime(_programFiles, "9.0.14");

        var result = DotNetRuntimeProbe.Probe(CreateOptions(DotNetRuntimeArchitecture.X64));

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void Probe_DetectsPerUserRuntime()
    {
        CreateRuntime(_localAppData, "10.0.5", DotNetRuntimeProbe.RequiredSharedFrameworkName);

        var result = DotNetRuntimeProbe.Probe(CreateOptions(DotNetRuntimeArchitecture.X64));

        Assert.True(result.IsAvailable);
        Assert.Contains(result.DetectedRuntimes, runtime =>
            runtime.Version == "10.0.5" &&
            runtime.Source == "shared-framework-directory-per-user");
    }

    [Fact]
    public void Probe_DetectsWindowsDesktopRuntime()
    {
        CreateRuntime(_programFiles, "10.0.5", DotNetRuntimeProbe.WindowsDesktopSharedFrameworkName);

        var result = DotNetRuntimeProbe.Probe(CreateOptions(DotNetRuntimeArchitecture.X64));

        Assert.False(result.IsAvailable);
        Assert.Contains(result.DetectedRuntimes, runtime =>
            runtime.Name == DotNetRuntimeProbe.WindowsDesktopSharedFrameworkName &&
            runtime.Version == "10.0.5");
    }

    [Fact]
    public void Probe_DetectsPerUserWindowsDesktopRuntime()
    {
        CreateRuntime(_localAppData, "10.0.5", DotNetRuntimeProbe.WindowsDesktopSharedFrameworkName);

        var result = DotNetRuntimeProbe.Probe(CreateOptions(DotNetRuntimeArchitecture.X64));

        Assert.Contains(result.DetectedRuntimes, runtime =>
            runtime.Name == DotNetRuntimeProbe.WindowsDesktopSharedFrameworkName &&
            runtime.Version == "10.0.5" &&
            runtime.Source == "shared-framework-directory-per-user");
    }

    [Fact]
    public void Probe_FindsDotNetHost_InPerUserPath()
    {
        var dotnetDir = Path.Combine(_localAppData, "dotnet");
        Directory.CreateDirectory(dotnetDir);
        File.WriteAllText(Path.Combine(dotnetDir, "dotnet.exe"), string.Empty);

        var result = DotNetRuntimeProbe.Probe(new DotNetRuntimeProbeOptions
        {
            Architecture = DotNetRuntimeArchitecture.X64,
            ProgramFilesPath = _programFiles,
            ProgramFilesX86Path = _programFilesX86,
            LocalAppDataPath = _localAppData,
            IncludeRegistry = false,
            IncludeDotNetCli = false
        });

        Assert.NotNull(result.DotNetHostPath);
        Assert.Contains("LocalAppData", result.DotNetHostPath);
    }

    [Fact]
    public void Probe_PrefersProgramFilesHost_OverPerUserHost()
    {
        var systemDotnetDir = Path.Combine(_programFiles, "dotnet");
        Directory.CreateDirectory(systemDotnetDir);
        File.WriteAllText(Path.Combine(systemDotnetDir, "dotnet.exe"), string.Empty);

        var perUserDotnetDir = Path.Combine(_localAppData, "dotnet");
        Directory.CreateDirectory(perUserDotnetDir);
        File.WriteAllText(Path.Combine(perUserDotnetDir, "dotnet.exe"), string.Empty);

        var result = DotNetRuntimeProbe.Probe(new DotNetRuntimeProbeOptions
        {
            Architecture = DotNetRuntimeArchitecture.X64,
            ProgramFilesPath = _programFiles,
            ProgramFilesX86Path = _programFilesX86,
            LocalAppDataPath = _localAppData,
            IncludeRegistry = false,
            IncludeDotNetCli = false
        });

        Assert.NotNull(result.DotNetHostPath);
        Assert.Contains("ProgramFiles", result.DotNetHostPath);
    }

    [Fact]
    public void Probe_CombinesSystemAndPerUserRuntimes()
    {
        CreateRuntime(_programFiles, "10.0.5");
        CreateRuntime(_localAppData, "10.0.3");

        var result = DotNetRuntimeProbe.Probe(CreateOptions(DotNetRuntimeArchitecture.X64));

        Assert.True(result.IsAvailable);
        Assert.Contains(result.DetectedRuntimes, runtime => runtime.Version == "10.0.5");
        Assert.Contains(result.DetectedRuntimes, runtime => runtime.Version == "10.0.3");
    }

    [Fact]
    public void ValidateDotNetRuntimePrerequisite_ReturnsStructuredFailure_WhenRuntimeIsMissing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var appDir = Path.Combine(_root, "app-1.0.0");
        Directory.CreateDirectory(appDir);
        var hostPath = Path.Combine(appDir, "LanMountainDesktop.exe");
        File.WriteAllText(hostPath, string.Empty);
        File.WriteAllText(Path.Combine(appDir, "LanMountainDesktop.runtimeconfig.json"), "{}");

        var plan = new HostLaunchPlan(
            hostPath,
            _root,
            appDir,
            [],
            new Dictionary<string, string>(),
            new() { Version = "1.0.0", Codename = "Test" });
        var resolution = new HostResolutionResult
        {
            Success = true,
            ResolvedHostPath = hostPath,
            AppRoot = _root,
            ResolutionSource = "test",
            SearchedPaths = [hostPath]
        };

        var result = LauncherFlowCoordinator.ValidateDotNetRuntimePrerequisite(
            plan,
            resolution,
            CreateOptions(DotNetRuntimeArchitecture.X64));

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("dotnet_runtime_missing", result.Code);
        Assert.Equal("False", result.Details["dotnetRuntimeAvailable"]);
    }

    private DotNetRuntimeProbeOptions CreateOptions(DotNetRuntimeArchitecture architecture)
    {
        return new DotNetRuntimeProbeOptions
        {
            Architecture = architecture,
            ProgramFilesPath = _programFiles,
            ProgramFilesX86Path = _programFilesX86,
            LocalAppDataPath = _localAppData,
            DotNetHostCandidates = [],
            IncludeRegistry = false,
            IncludeDotNetCli = false
        };
    }

    private static void CreateRuntime(string root, string version, string? frameworkName = null)
    {
        frameworkName ??= DotNetRuntimeProbe.RequiredSharedFrameworkName;
        Directory.CreateDirectory(Path.Combine(
            root,
            "dotnet",
            "shared",
            frameworkName,
            version));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
