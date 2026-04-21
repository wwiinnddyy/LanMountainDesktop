using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

internal sealed class LauncherClient
{
    private const int UserCanceledUacErrorCode = 1223;
    private const string LauncherExecutableName = "LanMountainDesktop.Launcher.exe";

    public async Task<LauncherInstallResult> InstallPackageAsync(
        string packagePath,
        string pluginsDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);

        if (!OperatingSystem.IsWindows())
        {
            return new LauncherInstallResult(
                false,
                null,
                "Elevated helper install is only supported on Windows.");
        }

        var launcherPath = ResolveLauncherPath();
        if (!File.Exists(launcherPath))
        {
            return new LauncherInstallResult(
                false,
                null,
                $"Launcher executable was not found at '{launcherPath}'.");
        }

        var resultPath = Path.Combine(
            Path.GetTempPath(),
            "LanMountainDesktop",
            "PluginInstallResults",
            $"{Guid.NewGuid():N}.json");

        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);

        try
        {
            using var process = StartLauncherProcess(launcherPath, packagePath, pluginsDirectory, resultPath);
            if (process is null)
            {
                return new LauncherInstallResult(false, null, "Failed to start launcher process.");
            }

            await process.WaitForExitAsync(cancellationToken);
            var result = await ReadResultAsync(resultPath, cancellationToken);
            if (result is not null)
            {
                return new LauncherInstallResult(result.Success, result.InstalledPackagePath, result.ErrorMessage);
            }

            if (process.ExitCode == 0)
            {
                return new LauncherInstallResult(
                    false,
                    null,
                    "Launcher exited without producing a result file.");
            }

            return new LauncherInstallResult(
                false,
                null,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Launcher exited with code {0}.",
                    process.ExitCode));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UserCanceledUacErrorCode)
        {
            return new LauncherInstallResult(false, null, "Administrator permission request was canceled.");
        }
        finally
        {
            TryDeleteFile(resultPath);
        }
    }

    private static Process? StartLauncherProcess(
        string launcherPath,
        string packagePath,
        string pluginsDirectory,
        string resultPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = launcherPath,
            Verb = "runas",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? AppContext.BaseDirectory,
            Arguments = string.Create(
                CultureInfo.InvariantCulture,
                $"--source {QuoteArgument(Path.GetFullPath(packagePath))} --plugins-dir {QuoteArgument(Path.GetFullPath(pluginsDirectory))} --result {QuoteArgument(Path.GetFullPath(resultPath))}")
        };

        return Process.Start(startInfo);
    }

    private static async Task<HelperResultFile?> ReadResultAsync(string resultPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(resultPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(resultPath);
        return await JsonSerializer.DeserializeAsync<HelperResultFile>(stream, cancellationToken: cancellationToken);
    }

    private static string ResolveLauncherPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Launcher", LauncherExecutableName);
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temp file cleanup failures.
        }
    }

    private sealed class HelperResultFile
    {
        public bool Success { get; init; }

        public string? InstalledPackagePath { get; init; }

        public string? ErrorMessage { get; init; }
    }
}

internal sealed record LauncherInstallResult(
    bool Success,
    string? InstalledPackagePath,
    string? ErrorMessage);
