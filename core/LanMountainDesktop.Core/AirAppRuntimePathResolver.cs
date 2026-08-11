namespace LanMountainDesktop.Shared.IPC;

public static class AirAppRuntimePathResolver
{
    private const string WindowsExecutableName = "LanMountainDesktop.AirAppRuntime.exe";
    private const string UnixExecutableName = "LanMountainDesktop.AirAppRuntime";
    private const string DllName = "LanMountainDesktop.AirAppRuntime.dll";

    private static string ExecutableName => OperatingSystem.IsWindows()
        ? WindowsExecutableName
        : UnixExecutableName;

    public static string? ResolveExecutablePath(string? appRoot = null, string? hostBaseDirectory = null)
    {
        return EnumerateCandidates(appRoot, hostBaseDirectory)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    public static IEnumerable<string> EnumerateCandidates(string? appRoot = null, string? hostBaseDirectory = null)
    {
        foreach (var root in EnumerateRoots(appRoot, hostBaseDirectory))
        {
            yield return Path.Combine(root, ExecutableName);
            yield return Path.Combine(root, DllName);
            yield return Path.Combine(root, "AirAppRuntime", ExecutableName);
            yield return Path.Combine(root, "AirAppRuntime", DllName);
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && current is not null; depth++, current = current.Parent)
        {
            yield return Path.Combine(
                current.FullName,
                "LanMountainDesktop.AirAppRuntime",
                "bin",
#if DEBUG
                "Debug",
#else
                "Release",
#endif
                "net10.0",
                ExecutableName);

            yield return Path.Combine(
                current.FullName,
                "LanMountainDesktop.AirAppRuntime",
                "bin",
#if DEBUG
                "Debug",
#else
                "Release",
#endif
                "net10.0",
                DllName);
        }
    }

    private static IEnumerable<string> EnumerateRoots(string? appRoot, string? hostBaseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(appRoot))
        {
            yield return Path.GetFullPath(appRoot);
        }

        if (!string.IsNullOrWhiteSpace(hostBaseDirectory))
        {
            var hostDirectory = Path.GetFullPath(hostBaseDirectory);
            yield return hostDirectory;
            yield return Path.GetFullPath(Path.Combine(hostDirectory, ".."));
        }

        yield return AppContext.BaseDirectory;
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    }
}
