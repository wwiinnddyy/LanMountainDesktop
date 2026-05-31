using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Services;

internal sealed record ElevatedPluginInstallResult(
    bool Success,
    string? Code,
    string? Message,
    string? ErrorMessage,
    string? InstalledPackagePath,
    string? ManifestId,
    string? ManifestName);

internal sealed class ElevatedPluginInstallService
{
    public async Task<ElevatedPluginInstallResult> InstallAsync(
        string sourcePackagePath,
        string pluginsDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);

        if (!OperatingSystem.IsWindows())
        {
            return new ElevatedPluginInstallResult(
                false,
                "elevation_unsupported",
                "Elevated plugin installation is only supported on Windows.",
                "Elevated plugin installation is only supported on Windows.",
                null,
                null,
                null);
        }

        var launcherPath = ResolveLauncherExecutablePath();
        if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
        {
            return new ElevatedPluginInstallResult(
                false,
                "launcher_not_found",
                "Launcher executable was not found for elevated plugin installation.",
                $"Launcher executable was not found. ResolvedPath='{launcherPath ?? string.Empty}'.",
                null,
                null,
                null);
        }

        var resultPath = Path.Combine(
            Path.GetTempPath(),
            $"LanMountainDesktop.PluginInstall.{Guid.NewGuid():N}.json");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launcherPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("plugin");
            startInfo.ArgumentList.Add("install");
            startInfo.ArgumentList.Add("--source");
            startInfo.ArgumentList.Add(Path.GetFullPath(sourcePackagePath));
            startInfo.ArgumentList.Add("--plugins-dir");
            startInfo.ArgumentList.Add(Path.GetFullPath(pluginsDirectory));
            startInfo.ArgumentList.Add("--result");
            startInfo.ArgumentList.Add(resultPath);

            var packageRoot = LauncherRuntimeMetadata.GetPackageRoot();
            if (!string.IsNullOrWhiteSpace(packageRoot))
            {
                startInfo.ArgumentList.Add("--app-root");
                startInfo.ArgumentList.Add(Path.GetFullPath(packageRoot));
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ElevatedPluginInstallResult(
                    false,
                    "launch_failed",
                    "Elevated plugin installer did not start.",
                    "Elevated plugin installer did not start.",
                    null,
                    null,
                    null);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (File.Exists(resultPath))
            {
                return ReadResult(resultPath);
            }

            return new ElevatedPluginInstallResult(
                process.ExitCode == 0,
                process.ExitCode == 0 ? "ok" : "installer_failed",
                process.ExitCode == 0 ? "Plugin installed." : $"Elevated installer exited with code {process.ExitCode}.",
                process.ExitCode == 0 ? null : $"Elevated installer exited with code {process.ExitCode}.",
                null,
                null,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new ElevatedPluginInstallResult(
                false,
                "elevation_cancelled",
                "Plugin installation was cancelled before elevation was approved.",
                ex.Message,
                null,
                null,
                null);
        }
        catch (Exception ex)
        {
            return new ElevatedPluginInstallResult(
                false,
                "elevation_failed",
                "Elevated plugin installation failed.",
                ex.Message,
                null,
                null,
                null);
        }
        finally
        {
            TryDelete(resultPath);
        }
    }

    private static ElevatedPluginInstallResult ReadResult(string resultPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(resultPath));
            var root = document.RootElement;
            return new ElevatedPluginInstallResult(
                GetBoolean(root, "Success"),
                GetString(root, "Code"),
                GetString(root, "Message"),
                GetString(root, "ErrorMessage"),
                GetString(root, "InstalledPackagePath"),
                GetString(root, "ManifestId"),
                GetString(root, "ManifestName"));
        }
        catch (Exception ex)
        {
            return new ElevatedPluginInstallResult(
                false,
                "invalid_result",
                "Elevated plugin installer returned an invalid result.",
                ex.Message,
                null,
                null,
                null);
        }
    }

    private static string? ResolveLauncherExecutablePath()
    {
        var candidates = new[]
        {
            LauncherRuntimeMetadata.GetPackageRoot(),
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))
        };

        foreach (var root in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            var path = Path.Combine(root!, OperatingSystem.IsWindows()
                ? "LanMountainDesktop.Launcher.exe"
                : "LanMountainDesktop.Launcher");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var property) &&
               property.ValueKind == JsonValueKind.True;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static void TryDelete(string path)
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
        }
    }
}
