using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LanMountainDesktop.Launcher.Infrastructure;

internal enum DotNetRuntimeArchitecture
{
    X64,
    X86
}

internal sealed record DotNetRuntimeInfo(
    string Name,
    string Version,
    string Source,
    string? Location);

internal sealed record DotNetRuntimeProbeOptions
{
    public int RequiredMajorVersion { get; init; } = 10;

    public DotNetRuntimeArchitecture Architecture { get; init; } = DotNetRuntimeProbe.GetCurrentArchitecture();

    public string? ProgramFilesPath { get; init; }

    public string? ProgramFilesX86Path { get; init; }

    public string? LocalAppDataPath { get; init; }

    public IReadOnlyList<string>? DotNetHostCandidates { get; init; }

    public bool IncludeRegistry { get; init; } = true;

    public bool IncludeDotNetCli { get; init; } = true;
}

internal sealed record DotNetRuntimeProbeResult(
    bool IsAvailable,
    int RequiredMajorVersion,
    DotNetRuntimeArchitecture Architecture,
    string? DotNetHostPath,
    IReadOnlyList<string> SearchedPaths,
    IReadOnlyList<DotNetRuntimeInfo> DetectedRuntimes,
    string Message)
{
    public Dictionary<string, string> ToDetails(string prefix = "dotnetRuntime")
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [$"{prefix}Available"] = IsAvailable.ToString(),
            [$"{prefix}RequiredMajorVersion"] = RequiredMajorVersion.ToString(),
            [$"{prefix}Architecture"] = Architecture.ToString(),
            [$"{prefix}DotNetHostPath"] = DotNetHostPath ?? string.Empty,
            [$"{prefix}SearchedPaths"] = string.Join(" | ", SearchedPaths),
            [$"{prefix}DetectedRuntimes"] = string.Join(
                " | ",
                DetectedRuntimes.Select(runtime =>
                    $"{runtime.Name} {runtime.Version} [{runtime.Source}{(string.IsNullOrWhiteSpace(runtime.Location) ? string.Empty : $": {runtime.Location}")}]")),
            [$"{prefix}Message"] = Message
        };
    }
}

internal static class DotNetRuntimeProbe
{
    public const string RequiredSharedFrameworkName = "Microsoft.NETCore.App";
    public const string WindowsDesktopSharedFrameworkName = "Microsoft.WindowsDesktop.App";

    private static readonly string[] RequiredSharedFrameworkNames =
    [
        RequiredSharedFrameworkName,
        WindowsDesktopSharedFrameworkName
    ];

    public static DotNetRuntimeProbeResult Probe(DotNetRuntimeProbeOptions? options = null)
    {
        options ??= new DotNetRuntimeProbeOptions();

        var searchedPaths = new List<string>();
        var detected = new List<DotNetRuntimeInfo>();
        var requiredMajor = options.RequiredMajorVersion;

        var localAppDataRoot = GetLocalAppDataPath(options);
        var perUserDotnetRoot = !string.IsNullOrWhiteSpace(localAppDataRoot)
            ? Path.Combine(localAppDataRoot, "dotnet")
            : null;

        foreach (var frameworkName in RequiredSharedFrameworkNames)
        {
            foreach (var basePath in EnumerateDotNetInstallRoots(options))
            {
                var sharedFrameworkDirectory = Path.Combine(basePath, "shared", frameworkName);
                searchedPaths.Add(sharedFrameworkDirectory);
                var isPerUser = perUserDotnetRoot is not null &&
                    string.Equals(basePath, perUserDotnetRoot, StringComparison.OrdinalIgnoreCase);
                AddDirectoryRuntimes(sharedFrameworkDirectory, frameworkName,
                    isPerUser ? "shared-framework-directory-per-user" : "shared-framework-directory",
                    detected);
            }
        }

        string? dotNetHostPath = null;
        foreach (var candidate in EnumerateDotNetHostCandidates(options))
        {
            searchedPaths.Add(candidate);
            if (dotNetHostPath is null && File.Exists(candidate))
            {
                dotNetHostPath = candidate;
            }
        }

        if (OperatingSystem.IsWindows() && options.IncludeRegistry)
        {
            foreach (var frameworkName in RequiredSharedFrameworkNames)
            {
                AddRegistryRuntimes(options.Architecture, frameworkName, detected);
            }
        }

        if (options.IncludeDotNetCli)
        {
            AddDotNetCliRuntimes(dotNetHostPath, detected);
        }

        var isAvailable = detected.Any(runtime =>
            string.Equals(runtime.Name, RequiredSharedFrameworkName, StringComparison.OrdinalIgnoreCase) &&
            IsRequiredMajor(runtime.Version, requiredMajor));

        var message = isAvailable
            ? $".NET {requiredMajor} runtime found for {options.Architecture}."
            : $".NET {requiredMajor} runtime was not found for {options.Architecture}.";

        return new DotNetRuntimeProbeResult(
            isAvailable,
            requiredMajor,
            options.Architecture,
            dotNetHostPath,
            searchedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            detected
                .DistinctBy(runtime => $"{runtime.Name}|{runtime.Version}|{runtime.Source}|{runtime.Location}", StringComparer.OrdinalIgnoreCase)
                .OrderBy(runtime => runtime.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(runtime => runtime.Version, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            message);
    }

    public static DotNetRuntimeArchitecture GetCurrentArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => DotNetRuntimeArchitecture.X86,
            _ => DotNetRuntimeArchitecture.X64
        };
    }

    public static string? FindDotNetHostPath(DotNetRuntimeProbeOptions? options = null)
    {
        options ??= new DotNetRuntimeProbeOptions();
        return EnumerateDotNetHostCandidates(options).FirstOrDefault(File.Exists);
    }

    public static bool IsFrameworkDependentWindowsApp(string executablePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var appName = Path.GetFileNameWithoutExtension(executablePath);
        var runtimeConfigPath = Path.Combine(directory, $"{appName}.runtimeconfig.json");
        if (!File.Exists(runtimeConfigPath))
        {
            return false;
        }

        return !File.Exists(Path.Combine(directory, "coreclr.dll")) &&
               !File.Exists(Path.Combine(directory, "hostfxr.dll")) &&
               !File.Exists(Path.Combine(directory, "hostpolicy.dll")) &&
               !File.Exists(Path.Combine(directory, "System.Private.CoreLib.dll"));
    }

    private static IEnumerable<string> EnumerateDotNetInstallRoots(DotNetRuntimeProbeOptions options)
    {
        var programFilesRoot = options.Architecture == DotNetRuntimeArchitecture.X86
            ? GetProgramFilesX86Path(options)
            : GetProgramFilesPath(options);

        yield return Path.Combine(programFilesRoot, "dotnet");

        var localAppData = GetLocalAppDataPath(options);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var perUserDotnet = Path.Combine(localAppData, "dotnet");
            if (!string.Equals(perUserDotnet, Path.Combine(programFilesRoot, "dotnet"), StringComparison.OrdinalIgnoreCase))
            {
                yield return perUserDotnet;
            }
        }
    }

    private static IEnumerable<string> EnumerateDotNetHostCandidates(DotNetRuntimeProbeOptions options)
    {
        if (options.DotNetHostCandidates is not null)
        {
            foreach (var candidate in options.DotNetHostCandidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    yield return Path.GetFullPath(candidate);
                }
            }

            yield break;
        }

        var programFilesRoot = options.Architecture == DotNetRuntimeArchitecture.X86
            ? GetProgramFilesX86Path(options)
            : GetProgramFilesPath(options);

        yield return Path.Combine(programFilesRoot, "dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        var localAppData = GetLocalAppDataPath(options);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var perUserHost = Path.Combine(localAppData, "dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (!string.Equals(perUserHost, Path.Combine(programFilesRoot, "dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"), StringComparison.OrdinalIgnoreCase))
            {
                yield return perUserHost;
            }
        }
    }

    private static string GetProgramFilesPath(DotNetRuntimeProbeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ProgramFilesPath))
        {
            return Path.GetFullPath(options.ProgramFilesPath);
        }

        return Environment.GetEnvironmentVariable("ProgramW6432") ??
               Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    }

    private static string GetProgramFilesX86Path(DotNetRuntimeProbeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ProgramFilesX86Path))
        {
            return Path.GetFullPath(options.ProgramFilesX86Path);
        }

        return Environment.GetEnvironmentVariable("ProgramFiles(x86)") ??
               Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }

    private static string GetLocalAppDataPath(DotNetRuntimeProbeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.LocalAppDataPath))
        {
            return Path.GetFullPath(options.LocalAppDataPath);
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    private static void AddDirectoryRuntimes(
        string sharedFrameworkDirectory,
        string sharedFrameworkName,
        string source,
        List<DotNetRuntimeInfo> detected)
    {
        if (!Directory.Exists(sharedFrameworkDirectory))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(sharedFrameworkDirectory))
        {
            var version = Path.GetFileName(directory);
            if (!string.IsNullOrWhiteSpace(version))
            {
                detected.Add(new DotNetRuntimeInfo(sharedFrameworkName, version, source, directory));
            }
        }
    }

    private static void AddRegistryRuntimes(
        DotNetRuntimeArchitecture architecture,
        string sharedFrameworkName,
        List<DotNetRuntimeInfo> detected)
    {
        try
        {
            var registryView = architecture == DotNetRuntimeArchitecture.X86
                ? RegistryView.Registry32
                : RegistryView.Registry64;
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
            using var key = baseKey.OpenSubKey(
                $@"SOFTWARE\dotnet\Setup\InstalledVersions\{(architecture == DotNetRuntimeArchitecture.X86 ? "x86" : "x64")}\sharedfx\{sharedFrameworkName}");

            if (key is null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is not null)
                {
                    detected.Add(new DotNetRuntimeInfo(sharedFrameworkName, valueName, "registry", key.Name));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to inspect .NET runtime registry keys: {ex.Message}");
        }
    }

    private static void AddDotNetCliRuntimes(
        string? dotNetHostPath,
        List<DotNetRuntimeInfo> detected)
    {
        if (string.IsNullOrWhiteSpace(dotNetHostPath) || !File.Exists(dotNetHostPath))
        {
            return;
        }

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = dotNetHostPath,
                Arguments = "--list-runtimes",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                var parsed = ParseListRuntimeLine(line);
                if (parsed is not null &&
                    RequiredSharedFrameworkNames.Contains(parsed.Value.Name, StringComparer.OrdinalIgnoreCase))
                {
                    detected.Add(new DotNetRuntimeInfo(
                        parsed.Value.Name,
                        parsed.Value.Version,
                        "dotnet-cli",
                        parsed.Value.Location));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to inspect .NET runtimes via dotnet CLI: {ex.Message}");
        }
    }

    private static (string Name, string Version, string? Location)? ParseListRuntimeLine(string line)
    {
        var firstSpace = line.IndexOf(' ');
        if (firstSpace <= 0 || firstSpace + 1 >= line.Length)
        {
            return null;
        }

        var secondSpace = line.IndexOf(' ', firstSpace + 1);
        if (secondSpace <= firstSpace)
        {
            return null;
        }

        var name = line[..firstSpace].Trim();
        var version = line[(firstSpace + 1)..secondSpace].Trim();
        var location = line[(secondSpace + 1)..].Trim().Trim('[', ']');
        return (name, version, string.IsNullOrWhiteSpace(location) ? null : location);
    }

    private static bool IsRequiredMajor(string version, int requiredMajor)
    {
        var dotIndex = version.IndexOf('.');
        var majorText = dotIndex < 0 ? version : version[..dotIndex];
        return int.TryParse(majorText, out var major) && major == requiredMajor;
    }
}
