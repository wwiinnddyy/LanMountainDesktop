using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

public static class AppRestartService
{
    private const string RestartParentPidArgumentPrefix = "--restart-parent-pid=";

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
        string? entryAssemblyLocation = null)
    {
        var args = commandLineArgs ?? Environment.GetCommandLineArgs();
        var resolvedProcessPath = NormalizeExistingPath(processPath ?? Environment.ProcessPath);
        var resolvedEntryAssemblyPath = NormalizeExistingPath(
            entryAssemblyLocation ?? Assembly.GetEntryAssembly()?.Location);

        if (IsDotnetHost(resolvedProcessPath))
        {
            return CreateDotnetStartInfo(
                resolvedProcessPath!,
                resolvedEntryAssemblyPath,
                args);
        }

        if (!string.IsNullOrWhiteSpace(resolvedProcessPath))
        {
            return CreateExecutableStartInfo(
                resolvedProcessPath,
                resolvedEntryAssemblyPath,
                args);
        }

        if (!string.IsNullOrWhiteSpace(resolvedEntryAssemblyPath) &&
            string.Equals(Path.GetExtension(resolvedEntryAssemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return CreateDotnetStartInfo(
                "dotnet",
                resolvedEntryAssemblyPath,
                args);
        }

        return null;
    }

    public static int? TryGetRestartParentProcessId(IReadOnlyList<string> commandLineArgs)
    {
        ArgumentNullException.ThrowIfNull(commandLineArgs);

        foreach (var argument in commandLineArgs)
        {
            if (TryParseRestartParentProcessId(argument, out var processId))
            {
                return processId;
            }
        }

        return null;
    }

    private static ProcessStartInfo CreateExecutableStartInfo(
        string executablePath,
        string? entryAssemblyPath,
        IReadOnlyList<string> commandLineArgs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            WorkingDirectory = ResolveWorkingDirectory(executablePath, entryAssemblyPath)
        };

        // UseShellExecute=true 时使用 Arguments 字符串而非 ArgumentList
        var args = new System.Text.StringBuilder();
        AppendArgumentsToString(args, commandLineArgs);
        AppendRestartParentProcessArgumentToString(args);
        startInfo.Arguments = args.ToString();
        return startInfo;
    }

    private static ProcessStartInfo? CreateDotnetStartInfo(
        string dotnetHostPath,
        string? entryAssemblyPath,
        IReadOnlyList<string> commandLineArgs)
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

        // UseShellExecute=true 时使用 Arguments 字符串
        var args = new System.Text.StringBuilder();
        args.Append(QuoteArgument(entryAssemblyPath));
        AppendArgumentsToString(args, commandLineArgs);
        AppendRestartParentProcessArgumentToString(args);
        startInfo.Arguments = args.ToString();
        return startInfo;
    }

    private static void AppendArguments(ProcessStartInfo startInfo, IReadOnlyList<string> commandLineArgs)
    {
        for (var i = 1; i < commandLineArgs.Count; i++)
        {
            if (TryParseRestartParentProcessId(commandLineArgs[i], out _))
            {
                continue;
            }

            startInfo.ArgumentList.Add(commandLineArgs[i]);
        }
    }

    private static void AppendArgumentsToString(System.Text.StringBuilder builder, IReadOnlyList<string> commandLineArgs)
    {
        for (var i = 1; i < commandLineArgs.Count; i++)
        {
            if (TryParseRestartParentProcessId(commandLineArgs[i], out _))
            {
                continue;
            }

            if (builder.Length > 0) builder.Append(' ');
            builder.Append(QuoteArgument(commandLineArgs[i]));
        }
    }

    private static void AppendRestartParentProcessArgument(ProcessStartInfo startInfo)
    {
        startInfo.ArgumentList.Add($"{RestartParentPidArgumentPrefix}{Environment.ProcessId}");
    }

    private static void AppendRestartParentProcessArgumentToString(System.Text.StringBuilder builder)
    {
        if (builder.Length > 0) builder.Append(' ');
        builder.Append($"{RestartParentPidArgumentPrefix}{Environment.ProcessId}");
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

        var builder = new System.Text.StringBuilder();
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

    private static bool TryParseRestartParentProcessId(string? argument, out int processId)
    {
        processId = 0;
        if (string.IsNullOrWhiteSpace(argument) ||
            !argument.StartsWith(RestartParentPidArgumentPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(
            argument[RestartParentPidArgumentPrefix.Length..],
            out processId) && processId > 0;
    }

    private static string? NormalizeExistingPath(string? path)
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
