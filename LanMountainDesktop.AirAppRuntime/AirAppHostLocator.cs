namespace LanMountainDesktop.AirAppRuntime;

internal sealed class AirAppHostLocator
{
    private const string WindowsExecutableName = "LanMountainDesktop.AirAppHost.exe";
    private const string UnixExecutableName = "LanMountainDesktop.AirAppHost";
    private const string DllName = "LanMountainDesktop.AirAppHost.dll";

    private static string ExecutableName => OperatingSystem.IsWindows()
        ? WindowsExecutableName
        : UnixExecutableName;

    public string Resolve(string? packageRoot, string? hostPath = null)
    {
        foreach (var candidate in EnumerateCandidates(packageRoot, hostPath))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Unable to find LanMountainDesktop.AirAppHost output.");
    }

    private static IEnumerable<string> EnumerateCandidates(string? packageRoot, string? hostPath)
    {
        foreach (var root in EnumerateRoots(packageRoot, hostPath))
        {
            yield return Path.Combine(root, "AirAppHost", ExecutableName);
            yield return Path.Combine(root, "AirAppHost", DllName);
            yield return Path.Combine(root, ExecutableName);
            yield return Path.Combine(root, DllName);

            if (Directory.Exists(root))
            {
                foreach (var deploymentDirectory in Directory.GetDirectories(root, "app-*", SearchOption.TopDirectoryOnly))
                {
                    yield return Path.Combine(deploymentDirectory, "AirAppHost", ExecutableName);
                    yield return Path.Combine(deploymentDirectory, "AirAppHost", DllName);
                    yield return Path.Combine(deploymentDirectory, ExecutableName);
                    yield return Path.Combine(deploymentDirectory, DllName);
                }
            }
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && current is not null; depth++, current = current.Parent)
        {
            yield return Path.Combine(
                current.FullName,
                "LanMountainDesktop.AirAppHost",
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
                "LanMountainDesktop.AirAppHost",
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

    private static IEnumerable<string> EnumerateRoots(string? packageRoot, string? hostPath)
    {
        if (!string.IsNullOrWhiteSpace(packageRoot))
        {
            yield return Path.GetFullPath(packageRoot);
        }

        if (!string.IsNullOrWhiteSpace(hostPath))
        {
            var hostDirectory = Path.GetDirectoryName(Path.GetFullPath(hostPath));
            if (!string.IsNullOrWhiteSpace(hostDirectory))
            {
                yield return hostDirectory;
            }
        }

        yield return AppContext.BaseDirectory;
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    }
}
