using System.Diagnostics;
using System.Reflection;
using System.Text;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Services;

public static class AppRestartService
{
    public static bool TryRestartApplication()
    {
        return App.CurrentHostApplicationLifecycle?.TryRestart(new HostApplicationLifecycleRequest(
            Source: nameof(AppRestartService),
            Reason: "Legacy restart entry point invoked.")) == true;
    }

    public static bool TryRestartCurrentProcess()
    {
        try
        {
            var startInfo = CreateRestartStartInfo();
            if (startInfo is null)
            {
                Debug.WriteLine("[AppRestart] Failed to resolve restart start info.");
                return false;
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppRestart] Failed to restart app: {ex}");
            return false;
        }
    }

    public static ProcessStartInfo? CreateRestartStartInfo(
        string[]? commandLineArgs = null,
        string? processPath = null,
        string? entryAssemblyLocation = null,
        RestartPresentationMode? restartPresentationMode = null)
    {
        var args = commandLineArgs ?? Environment.GetCommandLineArgs();
        var resolvedProcessPath = NormalizeExistingFile(processPath ?? Environment.ProcessPath);
        var resolvedEntryAssemblyPath = NormalizeExistingFile(
            entryAssemblyLocation ?? Assembly.GetEntryAssembly()?.Location);
        var normalizedRestartPresentation = restartPresentationMode
            ?? LauncherRuntimeMetadata.GetRestartPresentationMode(args)
            ?? RestartPresentationMode.Foreground;

        var launcherStartInfo = TryCreateLauncherStartInfo(
            args,
            resolvedProcessPath,
            resolvedEntryAssemblyPath,
            normalizedRestartPresentation);
        if (launcherStartInfo is not null)
        {
            return launcherStartInfo;
        }

        if (IsDotnetHost(resolvedProcessPath))
        {
            return CreateDotnetStartInfo(
                resolvedProcessPath!,
                resolvedEntryAssemblyPath,
                args,
                normalizedRestartPresentation);
        }

        if (!string.IsNullOrWhiteSpace(resolvedProcessPath))
        {
            return CreateExecutableStartInfo(
                resolvedProcessPath,
                resolvedEntryAssemblyPath,
                args,
                normalizedRestartPresentation);
        }

        if (!string.IsNullOrWhiteSpace(resolvedEntryAssemblyPath) &&
            string.Equals(Path.GetExtension(resolvedEntryAssemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return CreateDotnetStartInfo(
                "dotnet",
                resolvedEntryAssemblyPath,
                args,
                normalizedRestartPresentation);
        }

        return null;
    }

    public static int? TryGetRestartParentProcessId(IReadOnlyList<string> commandLineArgs)
    {
        ArgumentNullException.ThrowIfNull(commandLineArgs);
        return LauncherRuntimeMetadata.GetRestartParentProcessId(commandLineArgs);
    }

    public static RestartPresentationMode? TryGetRestartPresentationMode(IReadOnlyList<string> commandLineArgs)
    {
        ArgumentNullException.ThrowIfNull(commandLineArgs);
        return LauncherRuntimeMetadata.GetRestartPresentationMode(commandLineArgs);
    }

    private static ProcessStartInfo CreateExecutableStartInfo(
        string executablePath,
        string? entryAssemblyPath,
        IReadOnlyList<string> commandLineArgs,
        RestartPresentationMode restartPresentationMode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            WorkingDirectory = ResolveWorkingDirectory(executablePath, entryAssemblyPath)
        };

        var arguments = new StringBuilder();
        AppendForwardedArguments(arguments, commandLineArgs, restartPresentationMode);
        startInfo.Arguments = arguments.ToString();
        return startInfo;
    }

    private static ProcessStartInfo? CreateDotnetStartInfo(
        string dotnetHostPath,
        string? entryAssemblyPath,
        IReadOnlyList<string> commandLineArgs,
        RestartPresentationMode restartPresentationMode)
    {
        if (string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetHostPath,
            UseShellExecute = true,
            WorkingDirectory = ResolveWorkingDirectory(dotnetHostPath, entryAssemblyPath)
        };

        var arguments = new StringBuilder();
        arguments.Append(QuoteArgument(entryAssemblyPath));
        AppendForwardedArguments(arguments, commandLineArgs, restartPresentationMode);
        startInfo.Arguments = arguments.ToString();
        return startInfo;
    }

    private static ProcessStartInfo? TryCreateLauncherStartInfo(
        IReadOnlyList<string> commandLineArgs,
        string? processPath,
        string? entryAssemblyPath,
        RestartPresentationMode restartPresentationMode)
    {
        var launcherPath = ResolveLauncherPath(commandLineArgs, processPath, entryAssemblyPath);
        if (string.IsNullOrWhiteSpace(launcherPath))
        {
            return null;
        }

        var arguments = new StringBuilder();
        AppendFilteredArguments(arguments, commandLineArgs);
        AppendRestartArguments(arguments, restartPresentationMode);

        return new ProcessStartInfo
        {
            FileName = launcherPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? AppContext.BaseDirectory,
            Arguments = arguments.ToString()
        };
    }

    private static string? ResolveLauncherPath(
        IReadOnlyList<string> commandLineArgs,
        string? processPath,
        string? entryAssemblyPath)
    {
        var launcherFileName = OperatingSystem.IsWindows()
            ? "LanMountainDesktop.Launcher.exe"
            : "LanMountainDesktop.Launcher";

        foreach (var packageRootCandidate in GetPackageRootCandidates(commandLineArgs, processPath, entryAssemblyPath))
        {
            var normalizedRoot = NormalizeExistingDirectory(packageRootCandidate);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                continue;
            }

            var directCandidate = Path.Combine(normalizedRoot, launcherFileName);
            if (File.Exists(directCandidate))
            {
                return directCandidate;
            }
        }

        return null;
    }

    private static IEnumerable<string?> GetPackageRootCandidates(
        IReadOnlyList<string> commandLineArgs,
        string? processPath,
        string? entryAssemblyPath)
    {
        yield return LauncherRuntimeMetadata.GetPackageRoot(commandLineArgs);

        foreach (var path in new[] { entryAssemblyPath, processPath, AppContext.BaseDirectory })
        {
            var directory = GetDirectoryFromPath(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            yield return directory;
            yield return Path.GetDirectoryName(directory);
        }
    }

    private static string? GetDirectoryFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }

            return File.Exists(fullPath)
                ? Path.GetDirectoryName(fullPath)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void AppendForwardedArguments(
        StringBuilder builder,
        IReadOnlyList<string> commandLineArgs,
        RestartPresentationMode restartPresentationMode)
    {
        AppendFilteredArguments(builder, commandLineArgs);
        AppendRestartArguments(builder, restartPresentationMode);
    }

    private static void AppendFilteredArguments(StringBuilder builder, IReadOnlyList<string> commandLineArgs)
    {
        for (var index = 1; index < commandLineArgs.Count; index++)
        {
            if (ShouldSkipArgument(commandLineArgs, ref index))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(QuoteArgument(commandLineArgs[index]));
        }
    }

    private static bool ShouldSkipArgument(IReadOnlyList<string> commandLineArgs, ref int index)
    {
        var argument = commandLineArgs[index];
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        var key = argument[2..];
        var equalsIndex = key.IndexOf('=');
        if (equalsIndex >= 0)
        {
            key = key[..equalsIndex];
        }

        var shouldSkip = string.Equals(key, LauncherIpcConstants.LaunchSourceOptionName, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, LauncherIpcConstants.RestartParentPidOptionName, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, LauncherIpcConstants.RestartPresentationOptionName, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, LauncherIpcConstants.LauncherPidEnvVar, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, LauncherIpcConstants.PackageRootEnvVar, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, LauncherIpcConstants.VersionEnvVar, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, LauncherIpcConstants.CodenameEnvVar, StringComparison.OrdinalIgnoreCase);

        if (shouldSkip &&
            equalsIndex < 0 &&
            index + 1 < commandLineArgs.Count &&
            !commandLineArgs[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            index++;
        }

        return shouldSkip;
    }

    private static void AppendRestartArguments(StringBuilder builder, RestartPresentationMode restartPresentationMode)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append($"--{LauncherIpcConstants.LaunchSourceOptionName}=restart");
        builder.Append($" --{LauncherIpcConstants.RestartParentPidOptionName}={Environment.ProcessId}");
        builder.Append(
            $" --{LauncherIpcConstants.RestartPresentationOptionName}={LauncherRuntimeMetadata.FormatRestartPresentation(restartPresentationMode)}");
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Contains('"') && !value.Contains(' ') && !value.Contains('\t'))
        {
            return value;
        }

        var builder = new StringBuilder();
        builder.Append('"');
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                builder.Append("\\\"");
            }
            else
            {
                builder.Append(ch);
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string? NormalizeExistingFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsDotnetHost(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        var fileName = Path.GetFileName(processPath);
        return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveWorkingDirectory(string launchPath, string? entryAssemblyPath)
    {
        var basePath = !string.IsNullOrWhiteSpace(entryAssemblyPath)
            ? entryAssemblyPath
            : launchPath;

        return Path.GetDirectoryName(basePath) ?? AppContext.BaseDirectory;
    }
}
