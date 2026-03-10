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

internal sealed class PluginsInstallHelperClient
{
    private const int UserCanceledUacErrorCode = 1223;
    private const string HelperExecutableName = "LanMountainDesktop.PluginsInstallHelper.exe";

    public async Task<PluginsInstallHelperResult> InstallPackageAsync(
        string packagePath,
        string pluginsDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);

        if (!OperatingSystem.IsWindows())
        {
            return new PluginsInstallHelperResult(
                false,
                null,
                "Elevated helper install is only supported on Windows.");
        }

        var helperPath = ResolveHelperPath();
        if (!File.Exists(helperPath))
        {
            return new PluginsInstallHelperResult(
                false,
                null,
                $"Plugins install helper was not found at '{helperPath}'.");
        }

        var resultPath = Path.Combine(
            Path.GetTempPath(),
            "LanMountainDesktop",
            "PluginInstallResults",
            $"{Guid.NewGuid():N}.json");

        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);

        try
        {
            using var process = StartHelperProcess(helperPath, packagePath, pluginsDirectory, resultPath);
            if (process is null)
            {
                return new PluginsInstallHelperResult(false, null, "Failed to start plugins install helper.");
            }

            await process.WaitForExitAsync(cancellationToken);
            var result = await ReadResultAsync(resultPath, cancellationToken);
            if (result is not null)
            {
                return new PluginsInstallHelperResult(result.Success, result.InstalledPackagePath, result.ErrorMessage);
            }

            if (process.ExitCode == 0)
            {
                return new PluginsInstallHelperResult(
                    false,
                    null,
                    "Plugins install helper exited without producing a result file.");
            }

            return new PluginsInstallHelperResult(
                false,
                null,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Plugins install helper exited with code {0}.",
                    process.ExitCode));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UserCanceledUacErrorCode)
        {
            return new PluginsInstallHelperResult(false, null, "Administrator permission request was canceled.");
        }
        finally
        {
            TryDeleteFile(resultPath);
        }
    }

    private static Process? StartHelperProcess(
        string helperPath,
        string packagePath,
        string pluginsDirectory,
        string resultPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            Verb = "runas",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(helperPath) ?? AppContext.BaseDirectory,
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

    private static string ResolveHelperPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "PluginsInstallHelper", HelperExecutableName);
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

internal sealed record PluginsInstallHelperResult(
    bool Success,
    string? InstalledPackagePath,
    string? ErrorMessage);
