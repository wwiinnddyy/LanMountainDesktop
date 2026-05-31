using System.Diagnostics;
using System.Globalization;

namespace LanMountainDesktop.Shared.IPC;

public sealed record AirAppRuntimeStartRequest(
    string? AppRoot,
    int LauncherProcessId,
    int RequesterProcessId,
    string? DataRoot);

public static class AirAppRuntimeProcessStarter
{
    public static Process? Start(AirAppRuntimeStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runtimePath = AirAppRuntimePathResolver.ResolveExecutablePath(
            request.AppRoot,
            AppContext.BaseDirectory);
        if (string.IsNullOrWhiteSpace(runtimePath))
        {
            return null;
        }

        var startInfo = CreateStartInfo(runtimePath);
        AddOptionalArgument(startInfo, "--app-root", request.AppRoot);
        AddOptionalArgument(startInfo, "--data-root", request.DataRoot);
        AddIntArgument(startInfo, "--launcher-pid", request.LauncherProcessId);
        AddIntArgument(startInfo, "--requester-pid", request.RequesterProcessId);
        return Process.Start(startInfo);
    }

    public static ProcessStartInfo CreateStartInfo(string runtimePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimePath);
        var fullPath = Path.GetFullPath(runtimePath);
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory
        };

        var extension = Path.GetExtension(fullPath);
        if (OperatingSystem.IsWindows() &&
            string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = ResolveDotNetHostPath();
            startInfo.ArgumentList.Add(fullPath);
            return startInfo;
        }

        if (!OperatingSystem.IsWindows() &&
            string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(fullPath);
            return startInfo;
        }

        startInfo.FileName = fullPath;
        return startInfo;
    }

    private static string ResolveDotNetHostPath()
    {
        var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ??
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesCandidate = Path.Combine(programFiles, "dotnet", "dotnet.exe");
        if (File.Exists(programFilesCandidate))
        {
            return programFilesCandidate;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var perUserCandidate = Path.Combine(localAppData, "dotnet", "dotnet.exe");
        return File.Exists(perUserCandidate) ? perUserCandidate : "dotnet";
    }

    private static void AddOptionalArgument(ProcessStartInfo startInfo, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(Path.GetFullPath(value));
    }

    private static void AddIntArgument(ProcessStartInfo startInfo, string name, int value)
    {
        if (value <= 0)
        {
            return;
        }

        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value.ToString(CultureInfo.InvariantCulture));
    }
}
